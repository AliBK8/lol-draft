"""Adım 6: Havuz genişletme için aday listesi (CLAUDE.md sıradaki işler, H1)

Oyunun şampiyon dosyası (data/champions.json) elle onaylanır; bu betik onu DEĞİŞTİRMEZ. Yaptığı:
  - Data Dragon'dan tüm şampiyonlar: ad, sınıf etiketleri, saldırı menzili (anahtar gerekmez, herkese açık).
  - raw/ maçlarından (dereceli tek/çift) her şampiyonun hangi rolde kaç kez oynandığı (teamPosition).
  - Bunlardan bir TASLAK: kimlik, roller (oyunlarının ≥ %20'si), menzil, sınıf; havuzda olup olmadığı.
Çıktı: out/champion_candidates.json ve ekranda rol başına havuz dışındaki en çok oynananlar.

Taslak sadece başlangıç: menzil ve sınıf Data Dragon'un kaba sınıflaması (Gnar, Jayce, Kayle gibi form değiştirenler
elle düzeltilir), 500 maçta görünmeyen şampiyonun rolü boştur. Güç eğrisi ve etiketler bu adımda yok (H2, elle).

Kimlik: adın harfleri, küçük harf ("Kai'Sa" → "kaisa", "Wukong" → "wukong"); portre betiğiyle aynı kural
(web/scripts/fetch-champion-icons.mjs).

Çalıştırma:  python data-pipeline/06_champions.py
"""
import json
import sys
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

import requests

HERE = Path(__file__).parent
RAW = HERE / "raw"
OUT = HERE / "out"
POOL = HERE.parent / "data" / "champions.json"

DDRAGON = "https://ddragon.leagueoflegends.com"
RANKED_SOLO = 420
ROLE_SHARE = 0.20          # bir rol, şampiyonun oyunlarının en az %20'siyse rolü sayılır
RANGED_FROM = 300          # Data Dragon saldırı menzili: 125-175 melee, 500+ ranged
SHOW_PER_ROLE = 14

# Riot teamPosition → bizim rol adlarımız (champions.json ile aynı).
POSITIONS = {"TOP": "top", "JUNGLE": "jg", "MIDDLE": "mid", "BOTTOM": "adc", "UTILITY": "sup"}

# Data Dragon sınıfı → bizim sınıfımız (taslak; enchanter ile engage destek ayrımı elle yapılır).
CLASSES = {"Tank": "tank", "Fighter": "fighter", "Assassin": "assassin", "Mage": "mage",
           "Marksman": "marksman", "Support": "enchanter"}


def normalize(name: str) -> str:
    """Portre betiğindeki kural: aksanlar atılır, sadece a-z kalır."""
    plain = unicodedata.normalize("NFD", name.lower())
    return "".join(ch for ch in plain if "a" <= ch <= "z")


def fetch_ddragon() -> tuple[str, dict]:
    version = requests.get(f"{DDRAGON}/api/versions.json", timeout=30).json()[0]
    data = requests.get(f"{DDRAGON}/cdn/{version}/data/en_US/champion.json", timeout=30).json()["data"]
    return version, data


def role_counts(key_to_id: dict[str, str]) -> tuple[dict[str, Counter], int]:
    """Şampiyon kimliği → rol sayacı. Maç dosyaları: raw/{matchId}.json (timeline'lar _tl.json, atlanır)."""
    counts: dict[str, Counter] = defaultdict(Counter)
    matches = 0
    for path in sorted(RAW.glob("*.json")):
        if path.stem.endswith("_tl"):
            continue
        info = json.loads(path.read_text(encoding="utf-8"))["info"]
        if info.get("queueId") != RANKED_SOLO:
            continue
        matches += 1
        for p in info["participants"]:
            role = POSITIONS.get(p.get("teamPosition", ""))
            champion = key_to_id.get(p["championName"])
            if role and champion:
                counts[champion][role] += 1
    return counts, matches


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")   # Windows konsolu (cp1252) Türkçe karakterleri bozmasın
    version, data = fetch_ddragon()
    key_to_id = {c["id"]: normalize(c["name"]) for c in data.values()}
    pool = {c["id"] for c in json.loads(POOL.read_text(encoding="utf-8"))["champions"]}
    counts, matches = role_counts(key_to_id)

    candidates = []
    for c in data.values():
        cid = normalize(c["name"])
        played = counts.get(cid, Counter())
        games = sum(played.values())
        roles = [r for r, n in played.most_common() if games and n / games >= ROLE_SHARE]
        candidates.append({
            "id": cid,
            "name": c["name"],
            "ddragonKey": c["id"],
            "inPool": cid in pool,
            "games": games,
            "roleGames": dict(played.most_common()),
            "roles": roles,
            "range": "ranged" if c["stats"]["attackrange"] >= RANGED_FROM else "melee",
            "classes": [CLASSES[t] for t in c["tags"] if t in CLASSES][:2],
        })

    candidates.sort(key=lambda c: (-c["games"], c["id"]))
    unknown = sorted(pool - {c["id"] for c in candidates})
    OUT.mkdir(exist_ok=True)
    (OUT / "champion_candidates.json").write_text(
        json.dumps({"ddragonVersion": version, "matches": matches, "champions": candidates},
                   ensure_ascii=False, indent=1), encoding="utf-8")

    print(f"Data Dragon {version}: {len(candidates)} şampiyon · {matches} dereceli maç · havuzda {len(pool)}")
    if unknown:
        print(f"UYARI: havuzdaki şu kimlikler Data Dragon'da yok: {', '.join(unknown)}")
    for role in POSITIONS.values():
        rows = sorted((c for c in candidates if not c["inPool"] and role in c["roles"]),
                      key=lambda c: -c["roleGames"].get(role, 0))
        print(f"\n## {role} (havuz dışı, bu rolde en çok oynanan)")
        for c in rows[:SHOW_PER_ROLE]:
            other = ", ".join(r for r in c["roles"] if r != role)
            print(f"  {c['name']:<16} {c['roleGames'][role]:>4} oyun  {c['range']:<6} {'/'.join(c['classes']):<18}"
                  f"{'  (+ ' + other + ')' if other else ''}")
    unseen = [c["name"] for c in candidates if c["games"] == 0]
    print(f"\n500 maçta hiç oynanmayan: {len(unseen)} şampiyon")


if __name__ == "__main__":
    main()
