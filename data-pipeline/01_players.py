"""Adım 1: EUW Challenger + Grandmaster (solo/duo) oyuncularının puuid listesi → players.json

Çalıştırma:  python data-pipeline/01_players.py
"""
import json
from pathlib import Path

import riot

QUEUE = "RANKED_SOLO_5x5"
LEAGUES = ["challengerleagues", "grandmasterleagues"]
OUT = Path(__file__).parent / "players.json"


def main():
    players = {}  # puuid → kayıt (aynı oyuncu iki kez gelirse tekilleşir)
    for league in LEAGUES:
        data = riot.get(riot.PLATFORM, f"/lol/league/v4/{league}/by-queue/{QUEUE}")
        if data is None:
            print(f"{league}: bulunamadı, atlanıyor")
            continue
        tier = data["tier"]
        for e in data["entries"]:
            puuid = e.get("puuid")
            if not puuid:
                continue
            players[puuid] = {
                "puuid": puuid,
                "tier": tier,
                "lp": e["leaguePoints"],
                "games": e["wins"] + e["losses"],
            }
        print(f"{tier}: {len(data['entries'])} oyuncu")

    ordered = sorted(players.values(), key=lambda p: p["lp"], reverse=True)
    OUT.write_text(json.dumps(ordered, indent=1), encoding="utf-8")
    print(f"Toplam {len(ordered)} oyuncu → {OUT.name}")


if __name__ == "__main__":
    main()
