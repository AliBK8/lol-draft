# LoL Draft Oyunu

Arkadaşa link atıp LoL turnuva draft'ı (ban/pick, bo1/3/5, fearless) yapılan, ardından maçın gizli bir puan motoruyla simüle edilip haritada ikonlar ve olay log'uyla izlendiği bir web oyunu. Kâr amacı yok.

**Tüm tasarım, formüller ve kararlar: `docs/DESIGN.md`.** Bir şeyi değiştirmeden önce oraya bak; tasarım kararı değişirse DESIGN.md'yi de güncelle.

## Stack
- `src/LolDraft.Gateway` — YARP gateway: tek dış giriş, TLS, rate limiting, JWT doğrulama, güvenlik başlıkları
- `src/LolDraft.Api` — ASP.NET Core + SignalR (`DraftHub`), sadece iç ağda, SQLite
- `src/LolDraft.Application`, `LolDraft.Domain`, `LolDraft.Infrastructure` — katmanlar (DESIGN.md §8.2)
- `src/LolDraft.Engine` — simülasyon motoru (saf, seed'li, deterministik; dışarıya sadece `IMatchSimulator`)
- `web/` — React + TypeScript
- `data-pipeline/` — Python (requests; pandas yok, saf Python): Riot match-v5 timeline'larından hareket tabloları
- `prototype/mac-motoru.html` — çalışan JS prototip; v1 motor kurallarının referansı (ekonomi v2'den sonra C# motoru ondan ayrıldı, DESIGN.md §16)
- `tools/LolDraft.Balance` — denge aracı: rastgele ayna draft'larla motoru ölçer (DESIGN.md §9)
-- Hedef: .NET 10 (`net10.0`), SDK `global.json` ile sabit

## Temel kurallar
- Simülasyon sunucuda koşar; istemci sadece olay listesini oynatır.
- Maçı her zaman bir savaşı (kuşatma ya da 35. dk son savaş) kazanan bitirir. Puan sadece ihtimali etkiler.
- Olay ihtimalleri %10-%90 arasında sınırlı.
- Eğlence gerçekçilikten önce: geride olan takımın her zaman gerçek şansı olur; gerçek veri yön verir, hedefi belirlemez (DESIGN.md §1).
- Ekrandaki her olay (kill, kule, objektif) motorun puan değişiminin karşılığıdır (anlatıcı bütçesi, DESIGN.md §4.8).
- Riot API anahtarı `RIOT_API_KEY` ortam değişkeninde, sadece veri hattında; oyun sunucusu tutmaz.
- Dışarıdan erişim sadece gateway üzerinden; API iç ağda (DESIGN.md §8.1).
- Rate limiting iki katmanda: HTTP gateway'de, SignalR mesajları `IHubFilter` ile API'de (§8.5).
- Kapsülleme: varsayılan `internal` + `sealed`, public setter yok, değer nesneleri, iş hataları `Result<T>` (§8.3). Domain ve Engine ASP.NET/EF bilmez.
- Güvenlik kontrol listesi: DESIGN.md §8.6. Sırlar ortam değişkeninde, repoya girmez.

## Çalışma tarzı
- İletişim Türkçe, samimi.
- Kod yazmadan önce kavramı ve gerekçeyi açıkla; somut örnek ve adım adım izleme tercih edilir.
- Yanıtlar odaklı ve kısa olsun.
- Çalışan koda özellik eklerken **sadece o özelliğin gerektirdiğini değiştir**; ilgisiz satırları "düzeltme".
- Görev yerine getirildikten sonra bir kez daha üzerinde kontrol yapılıp sonrasında görev bitirilir ve kontrol edildiği belirtilir.

## Sıradaki işler
1. ~~Veri hattı 01-04, `zones.json`, `transitions.json`, `kill_heatmap.json`~~ (bitti)
2. ~~4a: Çözüm iskeleti (§8.2): `LolDraft.slnx`, 6 src + 3 test projesi, mimari testleri~~ (bitti)
3. ~~4b: C# motor iskeleti (sinerji + koridor + dk 35 son savaş), mulberry32, deterministiklik testleri~~ (bitti). Prototiple birebir port bitti; §14 bilinen sorunlar çözüldü (zaman çizelgesi, gank sınırı, kule = fırsat); nehir çatışması aralıklı/eşitsiz ve ejder ruhu/Elder eklendi (§4.5). Denge aracı bitti (§9).
4. Draft ve ekonomi v2 (DESIGN.md §16, sıra §16.13): ~~A ekonomi~~ (bitti) → B şampiyon modeli v2 + kurallar (bitti: model v2, K1-K13, karne, kural/kimlik raporu) → ~~C 40 şampiyon~~ (bitti: `tools/LolDraft.Balance/champions.json`) → D dengeleme (tur 1: K5, K6, kuşatma eşiği; tur 2: 15. dk 5.000+ önde %77 (savunma avantajı, yakalanma, s −%10); açık: erken comp %46,6, Dive %44, poke destekler, kural farkı 11+; DESIGN.md §16.13 adım 13)
5. ~~4c: Gateway (YARP) + API iletişimi, `GatewayOnlyMiddleware`~~ (bitti, DESIGN.md §8.10)
6. **← KALDIĞIMIZ YER (2026-11-03 sonu).** 4d: Draft durum makinesi (`LolDraft.Domain`, şu an boş). Plan kullanıcıyla konuşuldu:
   - ~~`DraftSession`: sıra tablosu, doğrulama, zaman aşımı, swap (yuva değiştir + kilitle); bitince `DraftResult` (Application `TeamDraft`'a çevirir)~~ (bitti, DESIGN.md §8.3 "Uygulama (4d)", 34 test). Kalan: seri + fearless kilitleri. Kullanıcıya açık 2 soru (2026-11-04 sonu): (a) koltuk ≠ renk: JWT'de `seat` Blue/Red yerine Host/Guest, renk her maç seriden (kaybeden seçer, soft fearless kilidi oyuncuya bağlı); (b) 40'lık havuzda full fearless bo5 sığmıyor: lobide kapatılsın mı, havuz mu büyüsün?
   - Her hamle doğrulanır (sıra, faz, havuz, tekrar, fearless); geçersizse durum değişmez, `Result.Fail`.
   - Zaman aşımı (boş ban / rastgele uygun pick), saat dışarıdan (`IClock`/`Instant`): test beklemeden.
   - Seri (bo1/3/5, kaybeden taraf seçer) ve fearless (full/soft) kilitleri; ban'lar sonraki maça taşınmaz.
   - Saf: ASP.NET, ağ, DB, BackgroundService yok (onlar lobi/hub adımında).
   - Karar (2026-11-04): sadece açık draft; gizli/eşzamanlı seçim modu yok.
7. 4e: Lobi uç noktaları + misafir JWT üretimi (Domain: Lobby, Application: CreateLobby/JoinLobby, Infrastructure: GuestTokenService), sonra DraftHub (SignalR, `IHubFilter` mesaj limiti, sunucu sayacı).
8. (v0.2) Prototipte/motorda gerçek `transitions.json` ile hareket
