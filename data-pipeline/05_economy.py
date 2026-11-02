"""Adım 5: Ekonomi kalibrasyonu (DESIGN.md §16.12, faz D adım 13)

raw/ maçlarından, denge aracının (tools/LolDraft.Balance) ölçtüğü sayıların gerçek oyundaki karşılığı:
  - "X. dakikada takım altını Y önde olan kazanıyor" eğrisi (10, 15, 20. dk; 0-1499, 1500-2999, 3000-4999, 5000+)
  - ortalama süre, maç başı kill, 14. dk ortalama CS (koridorcu + ormancı), 15. dk ortalama takım altın farkı
  - ejder ruhunu alan kazanıyor, 14. dk'da 3.000+ geride olan kurtarıyor
Çıktı: out/economy.json ve ekranda tablo.

- Sadece dereceli tek/çift (queue 420), 15 dakikadan uzun maçlar (erken teslim/remake dışarıda).
- Timeline karesi k ≈ k. dakikanın başı; totalGold başlangıç 500'ü içerir (motorda da öyle).
- 20. dakikadaki teslimler maçı orada bitirir; eğri "o dakikaya ulaşan maçlar" üzerinden (motorla aynı).

Çalıştırma:  python data-pipeline/05_economy.py
"""
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).parent
RAW = HERE / "raw"
OUT = HERE / "out"

RANKED_SOLO = 420
MIN_SECONDS = 15 * 60
CURVE_MINUTES = [10, 15, 20]
LEAD_BUCKETS = [0, 1500, 3000, 5000]
CS_MINUTE = 14
CRUSHED_MINUTE, CRUSHED_LEAD = 14, 3000


def bucket(lead: int) -> int:
    return max(b for b in LEAD_BUCKETS if abs(lead) >= b)


def pct(part: int, total: int) -> str:
    return "—" if total == 0 else f"%{100 * part / total:.1f}".replace(".", ",")


def margin(part: int, total: int) -> str:
    # %95 hata payı: ±1,96 × √(p(1−p)/n), denge aracıyla aynı.
    if total == 0:
        return ""
    p = part / total
    return f" ±{196 * math.sqrt(p * (1 - p) / total):.1f}".replace(".", ",")


def team_gold(frame: dict, team_ids: set[str]) -> int:
    return sum(pf["totalGold"] for pid, pf in frame["participantFrames"].items() if pid in team_ids)


def soul_team(frames: list) -> int | None:
    dragons = {100: 0, 200: 0}
    for frame in frames:
        for e in frame["events"]:
            if (e["type"] == "ELITE_MONSTER_KILL" and e["monsterType"] == "DRAGON"
                    and e.get("monsterSubType") != "ELDER_DRAGON" and e["killerTeamId"] in dragons):
                dragons[e["killerTeamId"]] += 1
                if dragons[e["killerTeamId"]] == 4:
                    return e["killerTeamId"]
    return None


def main() -> None:
    # Windows konsolu (cp1254) "→" yazamıyor.
    sys.stdout.reconfigure(encoding="utf-8")
    games = 0
    seconds = kills = 0
    curve: dict[tuple[int, int], list[int]] = {}       # (dk, dilim) → [maç, öndeki kazandı]
    cs_sum = cs_n = 0
    lead15_sum = lead15_n = 0
    soul_games = soul_wins = 0
    crushed_games = crushed_wins = 0

    for match_file in sorted(RAW.glob("*.json")):
        if match_file.name.endswith("_tl.json"):
            continue
        timeline_file = match_file.with_name(match_file.stem + "_tl.json")
        if not timeline_file.exists():
            continue
        info = json.loads(match_file.read_text(encoding="utf-8"))["info"]
        if info["queueId"] != RANKED_SOLO or info["gameDuration"] < MIN_SECONDS:
            continue
        frames = json.loads(timeline_file.read_text(encoding="utf-8"))["info"]["frames"]

        blue_won = next(t["win"] for t in info["teams"] if t["teamId"] == 100)
        blue = {str(p["participantId"]) for p in info["participants"] if p["teamId"] == 100}
        red = {str(p["participantId"]) for p in info["participants"] if p["teamId"] == 200}
        games += 1
        seconds += info["gameDuration"]
        kills += sum(p["kills"] for p in info["participants"])

        # Altın eğrisi: o dakikaya ulaşan maçlarda (son kare maçın bittiği an, sayılmaz) öndeki kazanıyor mu.
        for minute in CURVE_MINUTES:
            if minute >= len(frames) - 1:
                continue
            lead = team_gold(frames[minute], blue) - team_gold(frames[minute], red)
            if lead == 0:
                continue
            row = curve.setdefault((minute, bucket(lead)), [0, 0])
            row[0] += 1
            row[1] += (lead > 0) == blue_won

        # 14. dk CS: koridorcu + ormancı (support hariç), minyon + kamp.
        if CS_MINUTE < len(frames) - 1:
            roles = {str(p["participantId"]): p["teamPosition"] for p in info["participants"]}
            for pid, pf in frames[CS_MINUTE]["participantFrames"].items():
                if roles.get(pid) != "UTILITY":
                    cs_sum += pf["minionsKilled"] + pf["jungleMinionsKilled"]
                    cs_n += 1

        if 15 < len(frames) - 1:
            lead15_sum += abs(team_gold(frames[15], blue) - team_gold(frames[15], red))
            lead15_n += 1

        # Ejder ruhu: 4. ejderi ilk kesen takım (Elder hariç). DRAGON_SOUL_GIVEN ruhun alındığı an değil,
        # 2. ejderden sonra ruh türünün belirlendiği an (teamId 0).
        soul = soul_team(frames)
        if soul is not None:
            soul_games += 1
            soul_wins += (soul == 100) == blue_won

        # 14. dk'da 3.000+ geride olan kurtarıyor mu.
        if CRUSHED_MINUTE < len(frames) - 1:
            lead = team_gold(frames[CRUSHED_MINUTE], blue) - team_gold(frames[CRUSHED_MINUTE], red)
            if abs(lead) >= CRUSHED_LEAD:
                crushed_games += 1
                crushed_wins += (lead < 0) == blue_won

    if games == 0:
        sys.exit("raw/ içinde uygun maç yok (03_download.py).")

    result = {
        "games": games,
        "avgMinutes": round(seconds / games / 60, 1),
        "killsPerGame": round(kills / games, 1),
        "csAt14": round(cs_sum / max(1, cs_n), 1),
        "teamLeadAt15": round(lead15_sum / max(1, lead15_n)),
        "soul": {"games": soul_games, "wins": soul_wins},
        "crushedAt14": {"games": crushed_games, "comebacks": crushed_wins},
        "goldLeadCurve": [
            {"minute": m, "lead": b, "games": g, "leaderWins": w}
            for (m, b), (g, w) in sorted(curve.items())
        ],
    }
    OUT.mkdir(exist_ok=True)
    (OUT / "economy.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Gerçek maçlar — {games} maç (dereceli, 15 dk+)")
    print(f"  Ortalama süre                     {result['avgMinutes']} dk")
    print(f"  Maç başı kill                     {result['killsPerGame']}")
    print(f"  14. dk ortalama CS                {result['csAt14']}")
    print(f"  15. dk ortalama takım altın farkı {result['teamLeadAt15']}")
    print(f"  Ruhu alan kazanıyor               {pct(soul_wins, soul_games)}{margin(soul_wins, soul_games)} ({soul_games} maç)")
    print(f"  14. dk 3.000+ geride kurtarıyor   {pct(crushed_wins, crushed_games)}{margin(crushed_wins, crushed_games)} ({crushed_games} maç)")
    print()
    print("  Altın farkı → kazanma (o dakikada önde olan kazanıyor)")
    labels = {0: "0-1499", 1500: "1500-2999", 3000: "3000-4999", 5000: "5000+"}
    print("  fark        " + "".join(f"{m}. dk".ljust(26) for m in CURVE_MINUTES))
    for b in LEAD_BUCKETS:
        cells = []
        for m in CURVE_MINUTES:
            g, w = curve.get((m, b), (0, 0))
            cells.append(f"{pct(w, g)}{margin(w, g)} ({g})".ljust(26))
        print(f"  {labels[b]:<11} " + "".join(cells))


if __name__ == "__main__":
    main()
