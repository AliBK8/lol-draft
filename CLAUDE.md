# LoL Draft Oyunu

Arkadaşa link atıp LoL turnuva draft'ı (ban/pick, bo1/3/5, fearless) yapılan, ardından maçın gizli bir puan motoruyla simüle edilip haritada ikonlar ve olay log'uyla izlendiği bir web oyunu. Kâr amacı yok.

**Tüm tasarım, formüller ve kararlar: `docs/`.** Bir şeyi değiştirmeden önce oraya bak; tasarım kararı değişirse dokümanı da güncelle. § numaraları dosyalar arasında ortak; hangi § hangi dosyada, `docs/DESIGN.md` başındaki haritada:
- `DESIGN.md`: §0-7, §10-15 (ilkeler, karar günlüğü §1, motor v1, hareket, veri hattı, ön yüz)
- `SISTEM.md`: §8.1-8.9 (mimari, kapsülleme, rate limiting, güvenlik, API)
- `UYGULAMA.md`: §8.10+ (adım adım uygulama notları; **yeni adım bitince buraya yeni §8.x**)
- `EKONOMI-V2.md`: §16 (şampiyon modeli v2, ekonomi, kurallar K1-K13)
- `DENGE.md`: §9 + §16.13 (denge aracı, ölçümler, ayar turları)

## Stack
- `src/LolDraft.Gateway` — YARP gateway: tek dış giriş, TLS, rate limiting, JWT doğrulama, güvenlik başlıkları
- `src/LolDraft.Api` — ASP.NET Core + SignalR (`DraftHub`), sadece iç ağda, SQLite
- `src/LolDraft.Application`, `LolDraft.Domain`, `LolDraft.Infrastructure` — katmanlar (DESIGN.md §8.2)
- `src/LolDraft.Engine` — simülasyon motoru (saf, seed'li, deterministik; dışarıya sadece `IMatchSimulator`)
- `web/` — React + TypeScript (Vite). `npm run dev` (5173, proxy → gateway 5000), `npm test` (Vitest), `npm run icons` (Data Dragon portreleri), `npm run rakip -- <link>` / `-- --yeni bo3` (otomatik oynayan geliştirme rakibi)
- `data-pipeline/` — Python (requests; pandas yok, saf Python): Riot match-v5 timeline'larından hareket tabloları
- `prototype/mac-motoru.html` — çalışan JS prototip; v1 motor kurallarının referansı (ekonomi v2'den sonra C# motoru ondan ayrıldı, DESIGN.md §16)
- `tools/LolDraft.Balance` — denge aracı: rastgele ayna draft'larla motoru ölçer (DESIGN.md §9)
-- Hedef: .NET 10 (`net10.0`), SDK `global.json` ile sabit

## Temel kurallar
- Simülasyon sunucuda koşar; istemci sadece olay listesini oynatır.
- Maçı her zaman bir savaşı (kuşatma ya da 35. dk son savaş) kazanan bitirir. Draft ve altın (gerçek güç, DESIGN.md §16.5) sadece ihtimali etkiler.
- Olay ihtimalleri %10-%90 arasında sınırlı.
- Eğlence gerçekçilikten önce: geride olan takımın her zaman gerçek şansı olur; gerçek veri yön verir, hedefi belirlemez (DESIGN.md §1).
- Ekrandaki her olayın motorda bir karşılığı vardır: altın, güç ya da buff değiştirir (koridor kill'i ve plakası anlatıcı bütçesinden, DESIGN.md §4.8). Hiçbir şeyi değiştirmeyen olay gösterilmez. (`ScoreSheet` puanı v2'den beri karara girmiyor, sadece dökümde.)
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
Bitenler (ayrıntı ve kararlar dokümanda): veri hattı 01-05 · 4a çözüm iskeleti (§8.2) · 4b C# motoru + denge aracı (§4, §9) · ekonomi v2 faz A-C (§16, `data/champions.json`) · 4c gateway (§8.10) · 4d draft + seri (§8.3) · 4e-1 lobi + misafir JWT (§8.11) · 4e-2a/b/c hub, lobi saati, bağlantı takibi (§8.12-8.14) · 4f-1 simülasyon bağlantısı + rol dışı kumar (§8.15, §16.8) · 4f-2 kalıcı replay, EF Core + SQLite, saklama 180 gün (§8.16) · 5a ön yüz iskeleti, `GET /api/champions`, gateway'de istemci + SPA, Data Dragon portreleri (§8.17) · 5b canlı lobi, draft, swap, gizli skor, geliştirme rakibi (§8.18) · 5c maç oynatıcısı, karne, `/m/{id}` (§8.19).

1. **← KALDIĞIMIZ YER.** 6: Docker + CI (volume `/data/matches.db` + SQLite yedeği, §8.8).
2. Faz D dengeleme (§16.13 adım 13, DENGE.md), açık: erken comp %46,6, Dive %44, poke destekler, kural farkı 11+, rol dışı kumarın kaldıracı (§16.8).
3. Rejoin (ertelendi, gateway rotası hazır).
4. (v0.2) Prototipte/motorda gerçek `transitions.json` ile hareket.
