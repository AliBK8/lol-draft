"""Adım 3: Maç ve timeline verilerini indir → raw/{id}.json, raw/{id}_tl.json

match_ids.json sabit seed'le karıştırılır, ilk --limit kadarı alınır. Böylece
--limit büyütüldüğünde önceki seçim yenisinin içinde kalır. Var olan dosyalar
atlanır (kaldığı yerden devam). Dosyalar önce .tmp'ye yazılıp sonra yeniden
adlandırılır; yarıda kesilen indirme bozuk dosya bırakmaz.

Çalıştırma:  python data-pipeline/03_download.py [--limit 500]
"""
import argparse
import json
import random
import sys
from pathlib import Path

import riot

HERE = Path(__file__).parent
MATCH_IDS = HERE / "match_ids.json"
RAW = HERE / "raw"
SHUFFLE_SEED = 42  # değiştirme: değişirse seçilen maçlar da değişir


def download(match_id: str) -> tuple[int, int]:
    """Bir maçın iki dosyasını indirir. (indirilen, bulunamayan) sayısı döner."""
    got = missing = 0
    for suffix, path in (("", f"/lol/match/v5/matches/{match_id}"),
                         ("_tl", f"/lol/match/v5/matches/{match_id}/timeline")):
        target = RAW / f"{match_id}{suffix}.json"
        if target.exists():
            continue
        data = riot.get(riot.REGION, path)
        if data is None:
            missing += 1
            continue
        tmp = target.with_suffix(".tmp")
        tmp.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
        tmp.replace(target)
        got += 1
    return got, missing


def main():
    ap = argparse.ArgumentParser(description="Maç + timeline indir")
    ap.add_argument("--limit", type=int, default=500, help="indirilecek maç sayısı")
    args = ap.parse_args()
    if args.limit <= 0:
        sys.exit("--limit pozitif olmalı")
    if not MATCH_IDS.exists():
        sys.exit(f"{MATCH_IDS.name} yok, önce 02_match_ids.py'yi çalıştır")

    ids = json.loads(MATCH_IDS.read_text(encoding="utf-8"))
    random.Random(SHUFFLE_SEED).shuffle(ids)
    ids = ids[:args.limit]
    RAW.mkdir(exist_ok=True)

    files = missing = 0
    try:
        for i, match_id in enumerate(ids, 1):
            got, miss = download(match_id)
            files += got
            missing += miss
            if i % 25 == 0:
                print(f"{i}/{len(ids)} maç işlendi ({files} dosya indirildi)")
    except KeyboardInterrupt:
        print("Durduruldu; tekrar çalıştırınca kaldığı yerden devam eder")

    print(f"Bitti: {files} yeni dosya, {missing} bulunamadı (404) → {RAW.name}/")


if __name__ == "__main__":
    main()
