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
1. ~~Veri hattı 01-04, `zones.json`, `transitions.json`, `kill_heatmap.json`~~ (bitti)
2. ~~4a: Çözüm iskeleti (§8.2): `LolDraft.slnx`, 6 src + 3 test projesi, mimari testleri~~ (bitti)
3. ~~4b: C# motor iskeleti (sinerji + koridor + dk 35 son savaş), mulberry32, deterministiklik testleri~~ (bitti). Prototiple birebir port bitti; §14 bilinen sorunlar çözüldü (zaman çizelgesi, gank sınırı, kule = fırsat); nehir çatışması aralıklı/eşitsiz ve ejder ruhu/Elder eklendi (§4.5). Denge aracı bitti (§9).
4. Draft ve ekonomi v2 (DESIGN.md §16, sıra §16.13): ~~A ekonomi~~ (bitti) → B şampiyon modeli v2 + kurallar (bitti: model v2, K1-K13, karne, kural/kimlik raporu) → ~~C 40 şampiyon~~ (bitti: `data/champions.json`) → D dengeleme (tur 1: K5, K6, kuşatma eşiği; tur 2: 15. dk 5.000+ önde %77 (savunma avantajı, yakalanma, s −%10); açık: erken comp %46,6, Dive %44, poke destekler, kural farkı 11+; DESIGN.md §16.13 adım 13)
5. ~~4c: Gateway (YARP) + API iletişimi, `GatewayOnlyMiddleware`~~ (bitti, DESIGN.md §8.10)
6. ~~4d: Draft durum makinesi (`LolDraft.Domain`): `DraftSession` (sıra tablosu, doğrulama, zaman aşımı, swap) + `Series` (bo1/3/5, skor, renk değişimi, fearless kilitleri)~~ (bitti, DESIGN.md §8.3 "Uygulama (4d)" ve "Seri", 52 test). Kararlar:
   - (2026-11-04) Sadece açık draft; gizli/eşzamanlı seçim modu yok.
   - (2026-11-10) Soft fearless yok; fearless tek tür (iki takıma da kilit). Fearless + bo5 40'lık havuza sığmıyor, `Series.Start` reddediyor; havuz 60 olunca kendiliğinden açılır.
   - (2026-11-10) Kimlik `TeamSlot` A/B (Host/Guest değil: 2v2/turnuva modu), JWT `seat` = A/B. İlk mavi rastgele (sunucu), sonra renkler her maç değişir; "kaybeden seçer" kaldırıldı.
7. ~~4e-1: Lobi uç noktaları + misafir JWT (Domain: `Lobby`, `LobbyCode`, `PlayerId`; Application: `CreateLobby`/`JoinLobby`/`GetLobby` + portlar; Infrastructure: `GuestTokenService`, bellekte lobi deposu, gömülü şampiyon listesi)~~ (bitti, DESIGN.md §8.11). Kararlar (2026-11-10): aktif lobiler bellekte (`ILobbyRepository` arkasında, lobi başına kilit); rejoin ertelendi; açan A koltuğuna oturur. API artık `JWT_SIGNING_KEY` ister (gateway ile aynı değer).
8. 4e-2: DraftHub (SignalR). Kararlar (2026-11-10): draft iki oyuncu `Ready` deyince başlar (seride her maç); saniyelik `TimerTick` yok, durumda `deadline` + `serverNow`; seri başladıktan sonra 2 dk kesintisiz bağlı olmayan oyuncu seriyi hükmen kaybeder.
   - ~~4e-2a: hub, hamleler (Ready/Ban/Pick/SwapRoles/LockLineup) lobi kilidi altında, `actionId` tekrar koruması, `LobbyUpdated` DTO'su, API'de `GuestAuthenticationHandler`~~ (bitti, DESIGN.md §8.12).
   - ~~4e-2b: lobi saati (`LobbyClockService` → `Lobby.Advance` → `DraftSession.Timeout`), `RateLimitHubFilter` (sn'de 5, dk'da 60; dk'da 10 ihtar → bağlantı kapanır)~~ (bitti, DESIGN.md §8.13).
   - ~~4e-2c: bağlantı takibi + hükmen galibiyet (2 dk), ikisi birden yoksa lobi 5 dk sonra kapanır, 30 dk hareketsiz lobi kapanır (`LobbyClosed`), IP başına 3 lobi (gateway `X-Client-Ip`, depoda atomik `TryAdd`)~~ (bitti, DESIGN.md §8.14). Bu adımda 4c'den kalma bir açık bulundu ve düzeltildi: güvenilen proxy listesi boşken gateway herkesin `X-Forwarded-For`'una güveniyordu (§8.10).
9. **← KALDIĞIMIZ YER (2026-11-11).** 4f: Simülasyon bağlantısı: `DraftResult` → `TeamDraft` (katalogdan profil, rol dışı cezası), seed, motor, `RecordGame`, `MatchReady`.
   - Rol dışı cezası −%25 (DESIGN.md §16.8 sonu; `ChampionProfile.Roles`, karne satırı, denge aracına rol dışı oranı).
   - `Lobby.RecordGame` (Series'in değiştiren metotları artık internal, tek kapı Lobby; DESIGN.md §8.3).
   - Biten maçın olay listesi (`MatchResult`) SQLite'a yazılır, replay onu oynatır; seed + motor sürümü sadece teşhis için saklanır. Seed'le yeniden simüle etmek her denge ayarında ve platformlar arası `Math.Log/Cos` farkında eski linkleri bozar (analiz 2026-11-11).
10. 5: Ön yüz (`web/`, çirkin ama çalışan: lobi → draft → maç oynatıcı → seri). 6: Docker + CI.
11. (v0.2) Prototipte/motorda gerçek `transitions.json` ile hareket
