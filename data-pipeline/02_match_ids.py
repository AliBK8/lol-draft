"""Adım 2: players.json'daki oyunculardan ranked solo/duo maç id'leri → match_ids.json

Oyuncular LP sırasıyla gezilir; tekrarsız id sayısı hedefe ulaşınca durulur.
Ctrl+C ile durdurulursa o ana kadar toplananlar yine kaydedilir.

Çalıştırma:  python data-pipeline/02_match_ids.py [--target 2000] [--days 28]
"""
import argparse
import json
import sys
import time
from pathlib import Path

import riot

HERE = Path(__file__).parent
PLAYERS = HERE / "players.json"
OUT = HERE / "match_ids.json"
QUEUE = 420   # ranked solo/duo
COUNT = 20    # oyuncu başına istenen maç


def main():
    ap = argparse.ArgumentParser(description="Maç id'lerini topla")
    ap.add_argument("--target", type=int, default=2000, help="hedef tekrarsız maç sayısı")
    ap.add_argument("--days", type=int, default=28, help="son kaç günün maçları")
    args = ap.parse_args()
    if args.target <= 0 or args.days <= 0:
        sys.exit("--target ve --days pozitif olmalı")
    if not PLAYERS.exists():
        sys.exit(f"{PLAYERS.name} yok, önce 01_players.py'yi çalıştır")

    players = json.loads(PLAYERS.read_text(encoding="utf-8"))
    start_time = int(time.time()) - args.days * 86400
    params = {"queue": QUEUE, "count": COUNT, "startTime": start_time}

    ids = {}  # sıralı küme: id → None
    asked = 0
    try:
        for p in players:
            if len(ids) >= args.target:
                break
            got = riot.get(riot.REGION, f"/lol/match/v5/matches/by-puuid/{p['puuid']}/ids", params) or []
            ids.update(dict.fromkeys(got))
            asked += 1
            if asked % 10 == 0:
                print(f"{asked} oyuncu → {len(ids)} tekrarsız maç")
    except KeyboardInterrupt:
        print("Durduruldu, toplananlar kaydediliyor")

    OUT.write_text(json.dumps(list(ids), indent=1), encoding="utf-8")
    print(f"{asked} oyuncudan {len(ids)} tekrarsız maç → {OUT.name}")


if __name__ == "__main__":
    main()
