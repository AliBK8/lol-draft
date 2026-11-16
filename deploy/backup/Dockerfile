# SQLite yedekçisi (DESIGN.md §8.8): maç veritabanının volume'unu API ile paylaşır, yedekleri ayrı volume'a yazar.
FROM alpine:3.22
# API'yle aynı kullanıcı (1654, aspnet imajındaki app): WAL'daki -shm dosyasına ikisi de yazabilmeli.
# /data burada da 1654'ün: volume'u ilk bağlayan bu konteyner olursa sahibi yine doğru olur.
RUN apk add --no-cache sqlite \
    && addgroup -g 1654 app && adduser -u 1654 -G app -D -H app \
    && mkdir /data /backups && chown app:app /data /backups
COPY --chmod=755 backup.sh /usr/local/bin/backup.sh
USER 1654
ENTRYPOINT ["/usr/local/bin/backup.sh"]
