#!/bin/sh
# Maç veritabanının yedeği (DESIGN.md §8.8, UYGULAMA.md §8.20).
# Dosyayı düz kopyalamak (cp) güvenli değil: API o anda yazıyorsa ya da son yazılar henüz WAL'daysa kopya yarım ya da
# bozuk olur. sqlite3 ".backup" SQLite'ın kendi yedek API'si: tutarlı bir anlık görüntü alır; WAL'da okuyucu yazanı
# bekletmez, yani maç kaydı yedek yüzünden durmaz. Yedek önce geçici ada yazılır, integrity_check'ten geçerse asıl adını
# alır; yarım yedek hiç "yedek" gibi görünmez. En yeni BACKUP_KEEP tanesi kalır.
set -eu

DB=/data/matches.db
DIR=/backups
INTERVAL_HOURS=${BACKUP_INTERVAL_HOURS:-6}
KEEP=${BACKUP_KEEP:-28}

backup_once() {
  if [ ! -f "$DB" ]; then
    echo "veritabanı henüz yok ($DB), atlandı"
    return
  fi
  stamp=$(date -u +%Y%m%d-%H%M%S)
  tmp="$DIR/.matches-$stamp.tmp"
  out="$DIR/matches-$stamp.db"
  # .timeout: API o anda kilit tutuyorsa 5 sn'ye kadar tekrar dener.
  if sqlite3 -cmd ".timeout 5000" "$DB" ".backup '$tmp'" \
     && [ "$(sqlite3 "$tmp" 'PRAGMA integrity_check;')" = "ok" ]; then
    mv "$tmp" "$out"
    echo "yedek alındı: $out ($(du -h "$out" | cut -f1))"
    # Eskiler: ada göre sıralı (zaman damgası), en yeni KEEP tanesinin dışındakiler silinir.
    ls -1 "$DIR"/matches-*.db | sort -r | tail -n +"$((KEEP + 1))" | xargs -r rm -f --
  else
    rm -f "$tmp"
    echo "YEDEK BAŞARISIZ: $DB" >&2
  fi
}

# "once" argümanıyla bir kez alıp çıkar (elle yedek: docker compose run --rm backup once).
if [ "${1:-}" = "once" ]; then
  backup_once
  exit 0
fi

echo "yedekçi başladı: her $INTERVAL_HOURS saatte bir, son $KEEP yedek saklanır"
while true; do
  backup_once
  sleep "$((INTERVAL_HOURS * 3600))"
done
