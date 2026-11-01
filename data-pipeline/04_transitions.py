"""Adım 4: Bölge geçiş tablosu ve kill ısı haritası (DESIGN.md §5.2)

raw/ maçlarından:
  out/transitions.json   "rol|faz|durum|bölge" → sonraki dakikanın bölge olasılıkları
  out/kill_heatmap.json  "faz|bölge" → gerçek kill noktaları

- Bölgeler oyuncunun takımına göre kendi/rakip adlandırılır (top_outer_blue → mavi için
  top_outer_own, kırmızı için top_outer_enemy). Isı haritası mutlak bölgeleri kullanır.
- Oyuncunun ölü olduğu dakikalar alınmaz (motorda "ölü → base" kuralı var).
- Üç seviye satır yazılır: tam, durumsuz (*), faz+durumsuz (*|*). Motor tam satırda
  MIN_N'den az gözlem varsa bir üst seviyeye düşer.
- Satırda görülmemiş komşu bölgelere SMOOTH kadar sahte gözlem eklenir (taban olasılık).

Çalıştırma:  python data-pipeline/04_transitions.py
"""
import json
import math
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).parent
RAW = HERE / "raw"
OUT = HERE / "out"
ZONES = OUT / "zones.json"

ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"]
PHASES = [(0, 8, "0-8"), (8, 14, "8-14"), (14, 25, "14-25"), (25, 999, "25+")]
STATES = ["ahead", "even", "behind"]
GOLD_EDGE = 1500       # takım altın farkı eşiği
MIN_N = 30             # bir satırın kendi başına kullanılması için gereken gözlem
SMOOTH = 0.5           # görülmemiş komşu bölgeye sahte gözlem
MIN_DURATION = 15 * 60 # sn; daha kısa maçlar (remake) atılır

# Seviyeye göre temel yeniden doğma süresi (sn), 1..18
BRW = [10, 10, 12, 12, 14, 16, 20, 25, 28, 32.5, 35, 37.5, 40, 42.5, 45, 47.5, 50, 52.5]


def respawn_seconds(level: int, minutes: float) -> float:
    """Yaklaşık LoL yeniden doğma süresi: seviye tabanı + 15. dk sonrası zaman çarpanı."""
    base = BRW[max(1, min(18, level)) - 1]
    if minutes < 15:
        f = 0.0
    elif minutes < 30:
        f = math.ceil(2 * (minutes - 15)) * 0.00425
    elif minutes < 45:
        f = 0.1275 + math.ceil(2 * (minutes - 30)) * 0.003
    else:
        f = min(0.5, 0.2175 + math.ceil(2 * (minutes - 45)) * 0.0145)
    return base * (1 + f)


def phase_of(minute: float) -> str:
    return next(name for lo, hi, name in PHASES if lo <= minute < hi)


def state_of(gold_diff: float) -> str:
    return "ahead" if gold_diff >= GOLD_EDGE else "behind" if gold_diff <= -GOLD_EDGE else "even"


def relative(zone: str, team: str) -> str:
    """Mutlak bölge adını oyuncunun takımına göre kendi/rakip adına çevirir."""
    enemy = "red" if team == "blue" else "blue"
    return "_".join("own" if t == team else "enemy" if t == enemy else t for t in zone.split("_"))


class ZoneMap:
    def __init__(self, path: Path):
        d = json.loads(path.read_text(encoding="utf-8"))
        self.names, self.cells, self.grid, self.cell = d["zones"], d["cells"], d["grid"], d["cell"]
        if any(z < 0 for row in self.cells for z in row):
            sys.exit("zones.json'da boyanmamış hücre var")

    def at(self, x: float, y: float) -> str:
        c = min(self.grid - 1, max(0, int(x // self.cell)))
        r = min(self.grid - 1, max(0, int(y // self.cell)))
        return self.names[self.cells[r][c]]

    def adjacency(self) -> dict[str, set[str]]:
        """Kenar paylaşan hücrelerden mutlak bölge komşulukları (kendisi dahil)."""
        adj = defaultdict(set)
        g = self.grid
        for r in range(g):
            for c in range(g):
                a = self.names[self.cells[r][c]]
                adj[a].add(a)
                for rr, cc in ((r + 1, c), (r, c + 1)):
                    if rr < g and cc < g:
                        b = self.names[self.cells[rr][cc]]
                        adj[a].add(b)
                        adj[b].add(a)
        return adj


def load_match(match_id: str):
    """(maç, timeline) ya da atlanma nedeni (str) döner."""
    m_path, t_path = RAW / f"{match_id}.json", RAW / f"{match_id}_tl.json"
    if not t_path.exists():
        return "timeline yok"
    info = json.loads(m_path.read_text(encoding="utf-8"))["info"]
    if info["queueId"] != 420:
        return "ranked solo değil"
    if info["gameDuration"] < MIN_DURATION:
        return "15 dk'dan kısa"
    if any(p.get("gameEndedInEarlySurrender") for p in info["participants"]):
        return "erken teslim"
    if any(p["teamPosition"] not in ROLES for p in info["participants"]):
        return "rolü belirsiz oyuncu"
    return info, json.loads(t_path.read_text(encoding="utf-8"))["info"]


def death_windows(frames, levels_at) -> dict[int, list[tuple[float, float]]]:
    """Oyuncu → [(ölüm ms, dirilme ms)]."""
    out = defaultdict(list)
    for fi, fr in enumerate(frames):
        for e in fr.get("events", []):
            if e["type"] != "CHAMPION_KILL":
                continue
            pid, ts = e["victimId"], e["timestamp"]
            level = levels_at(max(0, fi - 1), pid)
            out[pid].append((ts, ts + respawn_seconds(level, ts / 60000) * 1000))
    return out


def main():
    if not ZONES.exists():
        sys.exit("out/zones.json yok, önce bölge boyama aracıyla oluştur")
    zmap = ZoneMap(ZONES)
    abs_adj = zmap.adjacency()

    # Kendi/rakip komşuluğu: iki takımın bakış açısının birleşimi
    rel_adj = defaultdict(set)
    for team in ("blue", "red"):
        for a, bs in abs_adj.items():
            rel_adj[relative(a, team)] |= {relative(b, team) for b in bs}
    rel_zones = sorted(rel_adj)

    counts = defaultdict(Counter)   # tam anahtar → sonraki bölge sayıları
    kills = defaultdict(list)       # "faz|bölge" → [[x, y, killer takımı]]
    skipped, patches = Counter(), Counter()
    used = dead_skipped = 0

    match_ids = sorted(p.name[:-5] for p in RAW.glob("*.json") if not p.name.endswith("_tl.json"))
    for n, match_id in enumerate(match_ids, 1):
        loaded = load_match(match_id)
        if isinstance(loaded, str):
            skipped[loaded] += 1
            continue
        info, tl = loaded
        used += 1
        patches[".".join(info["gameVersion"].split(".")[:2])] += 1

        # timeline participantId → (rol, takım); eşleme puuid ile
        by_puuid = {p["puuid"]: p for p in info["participants"]}
        players = {}
        for tp in tl["participants"]:
            p = by_puuid[tp["puuid"]]
            players[tp["participantId"]] = (p["teamPosition"], "blue" if p["teamId"] == 100 else "red")

        frames = tl["frames"]
        levels_at = lambda fi, pid: frames[fi]["participantFrames"][str(pid)]["level"]
        deaths = death_windows(frames, levels_at)

        for fi in range(len(frames) - 1):
            f0, f1 = frames[fi], frames[fi + 1]
            t0, t1 = f0["timestamp"], f1["timestamp"]
            minute = t0 / 60000
            phase = phase_of(minute)
            gold = Counter()
            for pid, (_, team) in players.items():
                gold[team] += f0["participantFrames"][str(pid)]["totalGold"]

            for pid, (role, team) in players.items():
                if any(d < t1 and r > t0 for d, r in deaths[pid]):
                    dead_skipped += 1
                    continue
                p0 = f0["participantFrames"][str(pid)].get("position")
                p1 = f1["participantFrames"][str(pid)].get("position")
                if not p0 or not p1:
                    continue
                enemy = "red" if team == "blue" else "blue"
                state = state_of(gold[team] - gold[enemy])
                z0 = relative(zmap.at(p0["x"], p0["y"]), team)
                z1 = relative(zmap.at(p1["x"], p1["y"]), team)
                counts[f"{role}|{phase}|{state}|{z0}"][z1] += 1

            for e in f1.get("events", []):
                if e["type"] == "CHAMPION_KILL" and "position" in e:
                    x, y = e["position"]["x"], e["position"]["y"]
                    victim_team = players[e["victimId"]][1]
                    killer_team = "red" if victim_team == "blue" else "blue"
                    kills[f"{phase_of(e['timestamp'] / 60000)}|{zmap.at(x, y)}"].append([x, y, killer_team])

        if n % 100 == 0:
            print(f"{n}/{len(match_ids)} maç okundu")

    if not used:
        sys.exit("Kullanılabilir maç yok")

    # Üç seviye: tam, durumsuz, faz+durumsuz
    levels = [dict(counts), defaultdict(Counter), defaultdict(Counter)]
    for key, c in counts.items():
        role, phase, _, zone = key.split("|")
        levels[1][f"{role}|{phase}|*|{zone}"].update(c)
        levels[2][f"{role}|*|*|{zone}"].update(c)

    def to_row(key: str, c: Counter) -> dict:
        zone = key.rsplit("|", 1)[1]
        weights = {z: float(v) for z, v in c.items()}
        for z in rel_adj[zone]:
            weights.setdefault(z, SMOOTH)
        total = sum(weights.values())
        nxt = {z: round(w / total, 4) for z, w in sorted(weights.items(), key=lambda kv: -kv[1])}
        return {"n": sum(c.values()), "next": nxt}

    rows = {}
    for lvl in levels:
        for key in sorted(lvl):
            rows[key] = to_row(key, lvl[key])

    OUT.mkdir(exist_ok=True)
    transitions = {
        "version": 1,
        "patches": sorted(patches),
        "matches": used,
        "min_n": MIN_N,
        "roles": ROLES,
        "phases": [name for _, _, name in PHASES],
        "states": STATES,
        "gold_edge": GOLD_EDGE,
        "zones": rel_zones,
        "adjacency": {z: sorted(rel_adj[z]) for z in rel_zones},
        "rows": rows,
    }
    (OUT / "transitions.json").write_text(json.dumps(transitions, indent=1) + "\n", encoding="utf-8")
    heat = {"version": 1, "matches": used, "phases": [name for _, _, name in PHASES],
            "note": "anahtar faz|mutlak bölge; nokta [x, y, kill alan takım], Riot koordinatı",
            "points": {k: kills[k] for k in sorted(kills)}}
    (OUT / "kill_heatmap.json").write_text(json.dumps(heat, separators=(",", ":")) + "\n", encoding="utf-8")

    # ---------------- rapor ----------------
    total = sum(sum(c.values()) for c in counts.values())
    sparse0 = sum(sum(c.values()) for c in levels[0].values() if sum(c.values()) < MIN_N)
    sparse1 = sum(sum(c.values()) for k, c in levels[0].items()
                  if sum(c.values()) < MIN_N and sum(levels[1][k.split("|")[0] + "|" + k.split("|")[1] + "|*|" + k.split("|")[3]].values()) < MIN_N)
    print()
    print(f"Maç: {used} kullanıldı, atlanan: {dict(skipped) or 'yok'}")
    print(f"Yama: {dict(sorted(patches.items()))}")
    print(f"Geçiş: {total} (ölü olduğu için alınmayan: {dead_skipped})")
    for name, lvl in zip(("tam", "durumsuz", "fazsız"), levels):
        full = sum(1 for c in lvl.values() if sum(c.values()) >= MIN_N)
        print(f"Satır ({name}): {len(lvl)}, {MIN_N}+ gözlemli: {full}")
    print(f"Kaba satıra düşen geçiş: %{sparse0 * 100 / total:.1f} durumsuza, %{sparse1 * 100 / total:.1f} fazsıza kadar")
    print(f"Kill noktası: {sum(len(v) for v in kills.values())}")
    print("→ out/transitions.json, out/kill_heatmap.json")


if __name__ == "__main__":
    main()
