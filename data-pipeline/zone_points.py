"""Bölge boyama aracı için doğrulama noktaları → tools/zone_points.js

raw/ timeline'larından oyuncu konumları, yıkılan bina ve kesilen büyük canavar
noktalarını çıkarır. Araç bu dosyayı varsa otomatik yükler; noktalar Riot
haritasındaki kulelerin / çukurların üstüne düşüyorsa koordinat eşlemesi doğrudur.

Çalıştırma:  python data-pipeline/zone_points.py [--max-matches 200]
"""
import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).parent
RAW = HERE / "raw"
OUT = HERE / "tools" / "zone_points.js"


def main():
    ap = argparse.ArgumentParser(description="Boyama aracı için veri noktaları")
    ap.add_argument("--max-matches", type=int, default=200, help="en fazla kaç timeline okunacak")
    args = ap.parse_args()

    files = sorted(RAW.glob("*_tl.json"))[:args.max_matches]
    if not files:
        sys.exit("raw/ içinde timeline yok, önce 03_download.py'yi çalıştır")

    positions, buildings, monsters = [], [], []
    for f in files:
        tl = json.loads(f.read_text(encoding="utf-8"))
        for frame in tl["info"]["frames"]:
            for pf in frame["participantFrames"].values():
                p = pf.get("position")
                if p:
                    positions.append([p["x"], p["y"]])
            for e in frame.get("events", []):
                p = e.get("position")
                if not p:
                    continue
                if e["type"] == "BUILDING_KILL":
                    buildings.append([p["x"], p["y"], e.get("towerType") or e.get("buildingType")])
                elif e["type"] == "ELITE_MONSTER_KILL":
                    monsters.append([p["x"], p["y"], e.get("monsterType")])

    data = {"matches": len(files), "positions": positions, "buildings": buildings, "monsters": monsters}
    OUT.write_text("window.ZONE_POINTS = " + json.dumps(data, separators=(",", ":")) + ";\n", encoding="utf-8")
    print(f"{len(files)} maç: {len(positions)} konum, {len(buildings)} bina, {len(monsters)} canavar → {OUT.relative_to(HERE)}")


if __name__ == "__main__":
    main()
