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
- `prototype/mac-motoru.html` — çalışan JS prototip; motor kurallarının referans uygulaması
-- Hedef: .NET 10 (`net10.0`), SDK `global.json` ile sabit

## Temel kurallar
- Simülasyon sunucuda koşar; istemci sadece olay listesini oynatır.
- Maçı her zaman bir savaşı (kuşatma ya da 35. dk son savaş) kazanan bitirir. Puan sadece ihtimali etkiler.
- Olay ihtimalleri %10-%90 arasında sınırlı.
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
3. ~~4b: C# motor iskeleti (sinerji + koridor + dk 35 son savaş), mulberry32, deterministiklik testleri~~ (bitti). Prototiple birebir port bitti; §14 bilinen sorunlar çözüldü (zaman çizelgesi, gank sınırı, kule = fırsat); nehir çatışması aralıklı/eşitsiz ve ejder ruhu/Elder eklendi (§4.5). Sırada: denge aracı (konsol, rastgele draft'lar, §9)
4. 4c: Gateway (YARP) + API iletişimi, `GatewayOnlyMiddleware`
5. MVP için 40 şampiyon ve etiketleri
6. (v0.2) Prototipte/motorda gerçek `transitions.json` ile hareket
