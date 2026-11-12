# Uygulama notları (§8.10 ve sonrası)

Tasarım dokümanının parçası; harita ve ilkeler: [DESIGN.md](DESIGN.md). Bölüm numaraları (§) bütün dosyalarda ortaktır. Her adım bittiğinde buraya yeni bir §8.x eklenir: bir isteğin/olayın yolu, "Konu | Nerede | Karar ve neden" tablosu, testler.

---

### 8.10 Uygulama: 4c (gateway + API hattı)

**Bir isteğin yolu:** İstemci → Gateway: iletilmiş başlıklar (sadece güvenilen proxy) → correlation id → güvenlik başlıkları (OnStarting) → gövde sınırı → HTTPS/HSTS (production) → yönlendirme (beyaz liste) → rate limit → JWT → rota politikası → YARP (istemcinin sır başlığı silinir, gerçek sır eklenir) → API: `GatewayOnlyMiddleware` → correlation id = TraceIdentifier → uç nokta.

| Önlem | Nerede | Karar ve neden |
|---|---|---|
| Sadece gateway'den | `Api/Middleware/GatewayOnlyMiddleware.cs` | `X-Gateway-Secret` tam bir değer ve eşleşmeli; ikisi de SHA-256'dan geçip `FixedTimeEquals` (zamanlama saldırısı, uzunluk sızıntısı yok). Red **404** (403 "burada bir şey var" der). |
| Sır taklidi | `Gateway/Proxy/GatewayTransforms.cs` | İstemcinin gönderdiği sır başlığı silinir, yerine gerçeği yazılır. |
| Beyaz liste | `Gateway/appsettings.json` `ReverseProxy:Routes` | Sadece listedeki yol + metot iletilir; API'ye eklenen uç nokta otomatik dışarı açılmaz. Bilinmeyen yol 404, yanlış metot 405, ikisi de API'ye ulaşmaz. |
| Rate limit | `Gateway/RateLimiting/GatewayRateLimiting.cs` | §8.5 tablosu (global token bucket + rota politikaları), IP'ye göre. **Kimlik doğrulamadan önce**: sel saldırısı imza doğrulamasına CPU harcatmaz. 429 + `Retry-After`. `simulate-heavy` ağır uçlar gelince. |
| JWT | `Gateway/Auth/GuestAuthentication.cs` | HS256 dışı algoritma reddedilir (`alg: none`), yayıncı `lol-draft`, hedef `lol-draft-game`, saat toleransı 30 sn (varsayılan 5 dk), `MapInboundClaims = false`. Hub yolunda token `access_token` sorgusundan, diğer yollarda sadece `Authorization` başlığından. `guest` politikası: imza + `lobby` + `seat` claim'i; yoksa 403. |
| Token loglanmaz | `appsettings.json` | `Microsoft.AspNetCore.Hosting.Diagnostics` ve `Yarp` Warning: Information seviyesi istek URL'sini (sorgudaki `access_token` dahil) yazardı. |
| İstemci IP'si | `Gateway/Program.cs` ForwardedHeaders | Varsayılan "localhost'a güven" de silinir; sadece `Gateway:TrustedProxies` listesi. Yoksa herkes `X-Forwarded-For`'a sahte IP yazıp rate limit'i atlatırdı. **Düzeltme (2026-11-11):** liste boşken iletilmiş başlıklar hiç işlenmez (`ForwardedHeaders.None`). İlk hâlde açık vardı: `KnownProxies` ve `KnownIPNetworks` ikisi de boşsa ASP.NET Core adres kontrolünü kapatıp herkese güvenir. TestServer'da uzak adres null olduğu için testler görmüyordu; test hattına gerçekçi istemci adresi verilince yakalandı (`LobbyFlowTests`, §8.14). |
| Başlıklar | `Gateway/Middleware/SecurityHeadersMiddleware.cs` | CSP (kendi origin + Google Fonts, `frame-ancestors 'none'`, `object-src 'none'`), nosniff, Referrer-Policy, X-Frame-Options, Permissions-Policy; `Server` silinir. OnStarting'de: proxy'lenen cevaba da girer, API ezemez. |
| Gövde sınırı | `Gateway/Middleware/RequestSizeLimitMiddleware.cs` + Kestrel | 16 KB; Content-Length büyükse gövde okunmadan 413, chunked'da Kestrel sınırı. |
| Correlation id | `Gateway/Middleware/CorrelationIdMiddleware.cs` | Gelen değer sadece `[A-Za-z0-9-]{1,64}` ise korunur (log enjeksiyonu), yoksa yeni GUID; API'ye iletilir, cevaba yazılır. |
| Sırlar | `GatewaySettings.cs`, `ApiSettings.cs` | `GATEWAY_SECRET` (≥ 32 karakter), `JWT_SIGNING_KEY` (≥ 32 bayt); eksik/zayıfsa uygulama açılmaz. |

**Uç noktalar (şu an):** sadece `GET /api/health` (API'de gerçek). Lobi, maç ve hub rotaları gateway'de tanımlı (politikalarıyla) ama API'de henüz uç noktaları yok (404); sıradaki adımda gelecekler.

**Testler:** `LolDraft.Api.Tests/GatewayOnlyTests` (8) ve yeni `LolDraft.Gateway.Tests` (21). Gateway testleri iki uygulamayı bellekte bağlar: YARP'ın HTTP istemcisi (`IForwarderHttpClientFactory`) API test sunucusunun handler'ına yönlendirilir; API'nin önündeki gözcü (`IStartupFilter`) API'ye hangi isteğin hangi sır başlığıyla ulaştığını kaydeder ("API'ye hiç ulaşmadı" kanıtlanabilir). Ayrıca gerçek süreçlerle (Kestrel + gerçek HTTP) duman testi yapıldı: sağlık 200 + başlıklar, API'ye doğrudan 404, tokensiz 401, beyaz liste dışı 404, 6. lobi 429, 20 KB 413.

**Geliştirmede çalıştırma:**
```
dotnet user-secrets set GATEWAY_SECRET "<en az 32 rastgele karakter>" --project src/LolDraft.Api
dotnet user-secrets set GATEWAY_SECRET "<aynı değer>"                  --project src/LolDraft.Gateway
dotnet user-secrets set JWT_SIGNING_KEY "<en az 32 rastgele karakter>" --project src/LolDraft.Gateway
dotnet user-secrets set JWT_SIGNING_KEY "<aynı değer>"                 --project src/LolDraft.Api
dotnet run --project src/LolDraft.Api       # http://localhost:5100 (iç)
dotnet run --project src/LolDraft.Gateway   # https://localhost:5001, http://localhost:5000 (dış)
```
Rastgele değer: `openssl rand -base64 48`. JWT anahtarı iki uygulamada aynı: API imzalar, gateway doğrular.

### 8.11 Uygulama: 4e-1 (lobi + misafir token)

**Lobi açmanın yolu:** `POST /api/lobbies {format, fearless}` → gateway (`lobby-create` limiti) → `LobbyEndpoints.Create` (format metni → `SeriesFormat`) → `CreateLobby`: CSPRNG'den kod (16 bayt), oyuncu kimliği (16 bayt) ve yazı tura (ilk mavi) → `Lobby.Create` (seri ayarı havuza sığıyor mu) → depoya ekle → token üret → `{code, seat: "A", token}`.

**Katılmanın yolu:** `POST /api/lobbies/{code}/join` → `JoinLobby`: yeni oyuncu kimliği → `ILobbyRepository.Execute(code, lobby => lobby.Join(player))` (lobinin kilidi altında) → koltuk alındıysa token → `{code, seat: "B", token}`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Lobi kuralları | `Domain/Lobby.cs` | Açan A'ya, katılan B'ye oturur; boş koltuk bir kez alınır (`lobby_full`). Lobi serinin sahibi; seri ayarı lobi açılırken doğrulanır (rakip davet edildikten sonra değil). |
| Kod ve kimlik | `Domain/LobbyCode.cs`, `PlayerId.cs` | 128 bit, base64url, 22 karakter. Rastgele baytlar dışarıdan gelir (Domain saf); istemciden gelen değer katı biçimden geçer. |
| Aktif lobiler bellekte | `Infrastructure/Lobbies/InMemoryLobbyRepository.cs` | Canlı draft her hamlede değişir, diske yazmak gereksiz. Bedeli: yeniden başlatmada açık lobiler düşer, tek API kopyası. Port async ve kilit portun içinde: kalıcı depoya geçiş sadece bu sınıfı değiştirir. |
| Eşzamanlılık | aynı | `Execute` işlemi lobi başına `lock` altında koşturur; `Lobby` eşzamanlılık bilmez. Aynı anda 10 katılmadan biri oturur (test). Canlı `Lobby` dışarı verilmez, okuma da kilit altında kopya (`LobbyView`) üretir. |
| Token | `Infrastructure/Auth/GuestTokenService.cs` | Üretir ve doğrular. Doğrulama gateway'le aynı kurallar (sadece HS256, yayıncı, hedef, süre, 30 sn tolerans). Claim'ler değer nesnelerinden geçer; `seat` sadece tam "A"/"B". |
| API de doğrular | `Api/Lobbies/LobbyEndpoints.cs` | Gateway imzayı doğrulayıp geçirir; API claim'leri imzasız okumaz, kendisi de doğrular. Sonra yetki: token'daki lobi = yoldaki lobi ve oyuncu o koltukta oturuyor mu (`not_in_lobby`, 403). |
| Hata → HTTP | aynı | `unauthorized` 401, `not_in_lobby` 403, `lobby_not_found` 404 (biçimi bozuk kod da), `lobby_full` 409, diğerleri (`pool_too_small`, `invalid_format`) 400. Gövde: `{error, message}`. |
| Şampiyon listesi | `Infrastructure/Champions/EmbeddedChampionCatalog.cs` | `data/champions.json` derlemeye gömülür (tek kaynak); şimdilik sadece kimlikler. Okuyucu `Infrastructure/Champions/ChampionFile.cs` (denge aracıyla ortak, 2026-11-11): bilinmeyen sınıf/rol, aralık dışı değer, geçersiz ya da tekrar eden kimlikte uygulama açılmaz. |
| Kayıt | `Infrastructure/InfrastructureServices.cs` | `AddInfrastructure(jwtSigningKey)` tek public kapı; gerçekler `internal sealed`. API açılışta `JWT_SIGNING_KEY` ister (≥ 32 bayt), yoksa açılmaz. |

### 8.12 Uygulama: 4e-2a (canlı draft hub'ı)

**Bir hamlenin yolu:** istemci `Pick("zed", actionId)` → (gateway: JWT, `hub-connect`) → API `GuestAuthenticationHandler` (token Bearer başlığından, hub yolunda başlık yoksa `access_token` sorgusundan) → `DraftHub.Pick` (metin → `ChampionId`, `Guid` → `ActionId`) → `LobbyCommands.Pick` → `ILobbyRepository.Execute` (lobi kilidi) → token'daki koltuk = lobideki koltuk mu → `Lobby.Pick(player, actionId, …)`: tekrar kontrolü, koltuk → bu maçtaki renk (`Series.SideOf`) → `DraftSession.Pick` → başarılıysa yeni durum (`LobbyView`) aynı kilidin altında üretilir → hamleyi yapana `{ok, error, message}`, gruptaki herkese `LobbyUpdated`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Draft başlangıcı | `Domain/Lobby.Ready` | İki koltuk da hazır deyince `Series.StartDraft`; hazırlar sıfırlanır, serinin sonraki maçında yeniden "hazır" denir (karar 2026-11-10). Lobi dolmadan `lobby_not_full`, maç sürerken `game_in_progress`. |
| Yetki ve renk | `Domain/Lobby` | Hamle oyuncu kimliğiyle gelir; lobide değilse `not_in_lobby`. Token renk taşımaz: koltuk her maç seriden renge çevrilir. |
| Tekrar koruması | `Domain/Lobby.Once`, `ActionId` | Aynı oyuncu + aynı `actionId` → ilk sonuç (başarılı ya da hatalı) aynen döner, hamle tekrarlanmaz. Lobi başına son 64 kimlik. Lobide olmayanın kimliği saklanmaz. Boş Guid `invalid_action_id`. |
| Kimlik doğrulama | `Api/Auth/GuestAuthenticationHandler.cs` | Doğrulamanın tek gerçeği `IGuestTokenService`; handler sadece token'ı bulur. REST GET de aynı handler'ı kullanır (`RequireAuthorization`). Hub `[Authorize]`: token yoksa negotiate 401. Token geçerli ama oyuncu lobide değilse `OnConnectedAsync` bağlantıyı kapatır. |
| Yayın | `Api/Hubs/DraftHub.cs` | Her lobi bir SignalR grubu (`lobby:{kod}`). Bağlanan önce güncel durumu alır. Başarısız hamle yayınlanmaz, sadece yapana hata döner. |
| Yayın sırası (`revision`) | `Domain/Lobby.Revision`, `LobbyStateDto.revision` | Görüntü kilit altında üretilir ama yayın kilidin dışında gider: aynı anda iki istek gelirse (iki `Ready`, hamle + saat tiki) yayınlar ağda yer değiştirebilir ve istemci eski durumda kalırdı (ör. draft başladı ama ekranda "hazır bekleniyor", oyuncu sırasını kaçırır). Lobi her görünür değişimde (oturma, bağlanma, kopma, başarılı yeni hamle, saatin değiştirdiği tik) sürümü bir artırır; başarısız ya da tekrar gelen hamle artırmaz. **İstemci kuralı:** elindekinden küçük ya da eşit `revision`'lı `LobbyUpdated`'ı yok sayar (2026-11-11). |
| Geri sayım | `LobbyStateDto.serverNow` + `draft.deadline` | Saniyelik mesaj yok: kalan = deadline − serverNow, istemci kendi saatiyle çizer (saat farkını serverNow'dan düzeltir). Süre dolunca karar sunucunun (4e-2b sayaç servisi). |
| JSON sözleşmesi | `Api/Lobbies/LobbyDtos.cs` | Domain tipleri doğrudan serileştirilmez; adlar elle çevrilir: faz `ban1…completed`, taraf `blue/red`, adım `ban/pick`, koltuk `A/B`, yuvalar `top, jungle, mid, bot, support`. |
| SignalR sınırları | `Api/Program.cs` | Mesaj ≤ 4 KB, canlılık 15 sn, istemci zaman aşımı 30 sn (§8.6). |

Testler: Domain (hazır, renk çevirisi, tekrar), Api (gerçek SignalR istemcisi, long polling: bağlanma, yayın, sıra dışı hamle, tekrar, geçersiz şampiyon, token'sız/bozuk token), Gateway (hub YARP üzerinden uçtan uca; token'sız hub isteği API'ye ulaşmaz).

### 8.13 Uygulama: 4e-2b (lobi saati + hub mesaj limiti)

**Lobi saati:** tek arka plan döngüsü, zamana bağlı her şey buradan (sayaç; 4e-2c'de hükmen galibiyet ve temizlik de). `LobbyClockService` (`PeriodicTimer`, 250 ms) → `AdvanceLobbies` → her lobi kendi kilidi altında `Lobby.Advance(now, random)` → durumu değişenler gruba `LobbyUpdated`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Karar Domain'de | `Domain/Lobby.Advance` | Adımın süresi dolduysa `DraftSession.Timeout` (boş ban / rastgele uygun pick / swap biter). Servis sadece zamanlar ve yayınlar. |
| Hamleyle yarış | aynı | İkisi de lobi kilidinden geçer. Süre dolduktan sonra gelen hamle `turn_expired` (ya da sıra çoktan geçmiş), süre dolmadan zaman aşımı olmaz: aynı adım iki kez oynanamaz. |
| Gecikme | `Api/Background/LobbyClockService.cs` | Süre dolduktan en geç ~250 ms sonra uygulanır. Tikteki hata loglanır, döngü durmaz (durursa hiçbir draft'ın süresi dolmaz). |
| Rastgele pick | `Infrastructure/InfrastructureServices.cs` | `IDraftRandom` = `RandomNumberGenerator.GetInt32`: tahmin edilemez (yoksa "süreyi bitirirsem X gelir" diye oynanırdı). |
| Ölçek | `Application/Lobbies/AdvanceLobbies.cs` | Her tikte bütün lobiler gezilir (`ILobbyRepository.Codes()`); yüzlerce lobi milisaniyeler. Binlerce olursa bitiş anına göre sıralı kuyruk. |
| Test | `LobbyClockTests` | `IClock` elle ilerletilir ("31 sn geçti"), döngü gerçek. Test saati gerçek "şimdi"den başlar: token o saatle imzalanır, doğrulama sistem saatine bakar. |

**Hub mesaj limiti** (`Api/Hubs/RateLimitHubFilter.cs`, `IHubFilter`): bağlantı başına saniyede 5, dakikada 60. Aşan mesaj hub metoduna hiç ulaşmaz, yapana `rate_limited` döner. Her ret bir ihtar; dakikada 10 ihtar → bağlantı kapatılır (karar 2026-11-10).
- Sayaç `SlidingWindow` (kayan pencere, olay zamanları kuyrukta): .NET'in hazır sınırlayıcıları otomatik yenileme için bağlantı başına zamanlayıcı açar, bu açmaz. Zaman `TimeProvider`'dan: test elle ilerletir.
- Sayaçlar bağlantının `Items`'ında yaşar, bağlantıyla birlikte gider. Kilit yok: SignalR bir bağlantının çağrılarını sırayla işler.
- Önce saniye, sonra dakika: saniyeye takılan mesaj dakika bütçesini harcamaz; reddedilen deneme pencereye yazılmaz (ısrar pencereyi uzatmaz).

### 8.14 Uygulama: 4e-2c (bağlantı takibi, hükmen galibiyet, kapanış, IP sınırı)

**Lobi saatinin kararları** (`Lobby.Advance`, sırayla; ilk tutan uygulanır):
1. **Kapanış:** kimse bağlı değilse son çıkıştan 5 dk sonra (karar 2026-11-11), ya da 30 dk hiç hareket yoksa (bağlı oyuncu olsa da, §8.6). Lobi depodan kilidin içinde silinir, bağlı kalan `LobbyClosed` alır. Lobi açılıp hiç bağlanılmazsa da 5 dk kuralı işler: oturan ama bağlanmamış koltuk "yok" sayılır.
2. **Hükmen galibiyet:** seri başladıktan sonra (ilk draft başladı) bir koltuk 2 dk kesintisiz yoksa ve rakibi bağlıysa `Series.Forfeit`: rakip seriyi alır, yarım draft atılır, `endReason: "forfeit"`. İkisi birden yoksa kimse kazanmaz, lobi 1. maddeyle kapanır.
3. **Zaman aşımı:** draft adımının süresi dolduysa (§8.13).

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Bağlantı sayımı | `Domain/Lobby.Connect/Disconnect` | Koltuk başına açık bağlantı sayısı: iki sekmeden biri kapanınca oyuncu "yok" sayılmaz. Sayı sıfıra düştüğü an `AbsentSince`. |
| Hub olayları | `Api/Hubs/DraftHub.cs` | `OnConnectedAsync` → `Connect` (lobide değilse bağlantı kapanır), `OnDisconnectedAsync` → `Disconnect`; ikisi de yeni durumu herkese yayınlar (rakip "bağlandı/ayrıldı" görür). Sadece sayılan bağlantı düşülür. |
| Gecikme | — | SignalR kopmayı sekme kapanınca hemen, ağ gidince en geç 30 sn'de fark eder (`ClientTimeoutInterval`): hükmen pratikte 2-2,5 dk. |
| Durumda | `LobbyStateDto.seats`, `endReason` | Koltuk başına `connected` ve `absentSince`: arayüz hükmen sayacını gösterir (absentSince + 2 dk − serverNow). |
| Hareket | `Lobby.LastActivity` | Her hamle (hatalı da), katılma, bağlanma, kopma. Zaman aşımı hareket sayılmaz. |
| Silme yarışı | `InMemoryLobbyRepository.Execute` | Kilidi beklerken lobi silinmişse işlem yapılmaz (`lobby_not_found`): kapanmış lobide hamle oynanmaz. |
| IP başına 3 lobi | `CreateLobby`, `ILobbyRepository.TryAdd` | Sayma ve ekleme tek kilit altında: aynı anda gelen 10 istekten tam 3'ü açar (test). Aşınca 429 `too_many_lobbies`. IP Domain'e girmez (kötüye kullanım kuralı, depo + Application). Lobi kapanınca sayaç düşer. |
| İstemci IP'si | `Gateway/Proxy/GatewayTransforms.cs`, API `X-Client-Ip` | Gateway istemcinin yazdığı `X-Client-Ip`'yi siler, kendi gördüğü adresi yazar (sır başlığıyla aynı desen). `X-Forwarded-For`'a güvenilmez. YARP istemci başlıklarını dönüşümlerden sonra tembel kopyalar: `ProxyRequest.Headers`'a doğrudan yazmak istemcinin değerini silmez; `RequestTransform.RemoveHeader/AddHeader` kullanılır. |

Bu adımda bulunan açık: gateway'in güvenilen proxy listesi boşken herkesin `X-Forwarded-For`'una güveniyordu (§8.10 "İstemci IP'si" satırı, düzeltildi; regresyon testi `Sahte_forwarded_for_ile_gateway_rate_limiti_atlatilamaz`).

Kalan: rejoin (ertelendi). Draft bitince simülasyon + `RecordGame` 4f'de.

### 8.15 Uygulama: 4f-1 (simülasyon bağlantısı)

**Bir maçın yolu:** ikinci `LockLineup` (ya da swap süresinin dolması, lobi saati) draft'ı bitirir → aynı lobi kilidinin içinde `PlayMatch.PlayIfDrafted`: swap sonrası dizilim → katalogdan profiller (`TeamDraft`, yuva sırası) → seed (CSPRNG) → `IMatchSimulator` → `Lobby.RecordGame(kazanan renk, MatchId)` → kilit bırakılır → `IMatchRepository.Save` → gruba önce `MatchReady` (maç), sonra `LobbyUpdated` (skor, `games`).

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Kilit altında simülasyon | `Application/Matches/PlayMatch.cs` | "Draft bitti" ile "sonuç yazıldı" arasına hamle ya da tik giremez. Simülasyon ~1 ms, kilidi uzatmaz. Hamle yolu (`LobbyCommands`) ve saat yolu (`AdvanceLobbies`) aynı servisi çağırır. |
| Yazma ve yayın sırası | `LobbyCommands`, `AdvanceLobbies`, `DraftHub.Publish`, `LobbyClockService` | Kayıt kilidin dışında (depo async) ama yayından önce: link yayınlandığında maç okunabilir. Önce `MatchReady`, sonra skoru taşıyan `LobbyUpdated`. |
| Skorun açıklanması | istemci | Sunucu sonucu hemen yazar ve yayınlar; istemci maçı oynatırken skoru, `games`'teki son kazananı ve seri sonucunu oynatma bitene (ya da "sona atla"ya) kadar göstermez (karar 2026-11-12). Sonucu erken görmek avantaj sağlamaz (maç oynanmış). |
| Maç kimliği | `Domain/MatchId` | 128 bit rastgele (lobi kodu gibi): tekrar izleme linki token istemez, tahmin edilemezliği gizliliğidir. `SeriesGame.Match` ile seride saklanır. |
| Seed | `PlayMatch` | `ISecureRandom`'dan 32 bit; istemciye hiç gitmez (§8.6). Kayıtta teşhis için durur, replay onu kullanmaz. |
| Motor sürümü | `MatchRecord.EngineVersion` | Motor derlemesinin MVID'si: deterministik derlemede kod değişmeden değişmez. Teşhis içindir (hangi denge ayarıyla oynandı). |
| Kayıt | `IMatchRepository` → `Infrastructure/Matches/InMemoryMatchRepository` | 4f-1: bellekte, en fazla 2.000 maç (en eskisi atılır; maç ~50 KB). Sunucu yeniden başlarsa replay'ler düşer. 4f-2'de SQLite (port aynı). |
| JSON sözleşmesi | `Api/Matches/MatchDtos.cs` | Motor tipleri doğrudan serileştirilmez; olaylar §4.9 adlarıyla (`kill`, `tower`, …, `split`, `dragonSoul`) elle çevrilir. Bilinmeyen olay tipi hata fırlatır (sessizce kaybolmasın). Seed ve `ScoreBreakdown` dışarı çıkmaz. `blueTeam` koltuğu verir: istemci rengi koltuğa çevirir. |
| Tekrar izleme ucu | `Api/Matches/MatchEndpoints.cs` | `GET /api/matches/{id}`: token yok, gateway'de `replay` limiti (IP başına dk'da 60). Bozuk ya da olmayan kimlik ikisi de 404 `match_not_found`. |
| Hata → HTTP | `Api/ApiErrors.cs` | Bütün uç noktaların ortak tablosu (lobi + maç). |

Testler: Domain (`Lobby.RecordGame`: skor, renk değişimi, sürüm, draft bitmeden reddedilir), Api (gerçek SignalR: iki oyuncu 20 adımı oynar ve kilitler → ikisine de aynı `MatchReady`, `games`'te kimlik ve doğru koltuk, seed yok, `GET` aynı maçı döner; saat yolu: kimse oynamazsa 21 zaman aşımı sonra maçı saat oynatır, bo1 seri biter; olmayan maç 404), Gateway (replay rotası token'sız API'ye ulaşır).

### 8.16 Uygulama: 4f-2 (kalıcı replay, EF Core + SQLite)

**Bir maçın yolu (değişen kısım):** `PlayMatch` simülasyondan hemen sonra, seriye yazmadan önce maçı **bir kez** sözleşme JSON'una çevirir ve dondurur (`Replay`: sürüm + UTF-8 baytlar) → kilidin dışında `IMatchRepository.Save` → satır Brotli ile sıkıştırılıp `Matches` tablosuna → `MatchReady` aynı baytları yayınlar → `GET /api/matches/{id}` aynı baytları diskten olduğu gibi döner. Canlı yayın, kayıt ve tekrar izleme tek belgedir.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Ne saklanır | `Application/Matches/MatchReplay.cs` (`Replay`, `MatchReplay`) | İstemcinin aldığı JSON, motorun `MatchResult`'ı değil (karar 2026-11-12). Motor tipleri denge ayarıyla değişir; dondurulmuş belge eski maçı değiştirmez, okurken yeniden çeviri gerekmez. Belgenin ilk alanı `version` (şu an 1; DB'de `ReplayVersion` sütunu da aynı): sözleşme kırılırsa artar, istemci eski sürümü buna bakıp tanır. |
| Çeviri nerede | `Application/Matches/MatchReplay.cs` (eski `Api/Matches/MatchDtos.cs`) | Kaydı yazan Application olduğu için çeviri Api'den taşındı. Biçim değişmedi: `JsonSerializerDefaults.Web` (4f-1'deki `Results.Ok` ile aynı camelCase). Belge `RecordGame`'den önce üretilir: çeviri hata verirse (sözleşmede olmayan olay) skor da yazılmaz. |
| Yayın | `Api/Matches/ReplayPayload.cs`, `DraftHub.Publish`, `LobbyClockService` | `MatchReady` gövdesi belgeden okunan `JsonElement`; SignalR olduğu gibi yazar. Test: canlı belge ile GET'ten gelen `JsonElement.DeepEquals`. |
| Tablo | `Infrastructure/Matches/MatchRow.cs`, `MatchDbContext.cs` | `Matches`: `Id` (anahtar), `Lobby`, `GameNumber`, `BlueTeam`, `Seed`, `EngineVersion`, `PlayedAt` (Unix ms, indeksli: SQLite tarih tipi bilmez), `ReplayVersion`, `Replay` (Brotli BLOB). Ölçüm (20 maç): belge ~32 KB, sıkıştırılmış ~5,3 KB (6 kat). Bozuk satır programcı hatası: okurken fırlatır. |
| Depo | `Infrastructure/Matches/SqliteMatchRepository.cs` | Singleton depo, her işlem kendi `DbContext`'iyle (`IDbContextFactory`; DbContext iş parçacığı güvenli değil). Sorgular EF ile parametreli; silme tek `DELETE … WHERE PlayedAt < @p` (`ExecuteDeleteAsync`). |
| Kayıt hatası | `IMatchRepository.Save` sözleşmesi | **Fırlatmaz, loglar.** 4f-1'de bellekte hata olmuyordu; diskte olabilir. Fırlatsaydı saat tikinde diğer lobilerin tiki ve yayınları yarıda kalır, hamlede `MatchReady` gitmezdi (skor zaten yazılmışken). Yazılamayan maçın sadece linki 404 verir. Logda maç kimliği yok (linkin kendisi, §8.6). |
| İptal | `LobbyCommands`, `AdvanceLobbies` | `Save` `CancellationToken.None` ile: hamlenin token'ı hamleyi yapanın bağlantısıdır; o tam bu anda koparsa oynanmış maç kaydedilmeden kalırdı. |
| Migration | `Infrastructure/Matches/Migrations`, `InfrastructureServices.InitializeAsync` | API açılırken `MigrateAsync` (istek kabul etmeden önce); şema uyuşmazsa uygulama açılmaz. Dosyada `PRAGMA journal_mode=WAL` (okuma yazmayı beklemez; sabit PRAGMA, girdisiz). Yeni migration: `dotnet ef migrations add <Ad> --project src/LolDraft.Infrastructure --startup-project src/LolDraft.Infrastructure --output-dir Matches/Migrations --namespace LolDraft.Infrastructure.Matches.Migrations` (araç repoda: `dotnet-tools.json`, dotnet-ef 10.0.14; tasarım zamanı fabrikası `MatchDbContextDesignFactory`). |
| Yer | `ApiSettings` `MATCH_DB_PATH`, `Infrastructure/Matches/MatchStorage.cs` | Dosya yolu; klasör yoksa açılır. Üretimde zorunlu (unutulursa replay'ler sessizce kaybolmasın). Geliştirme/test ortamında verilmezse bellekte (her uygulama örneğine ayrı adlı paylaşılan bellek veritabanı; açık tutan "çapa" bağlantı). `launchSettings`: `data/local/matches.db` (`*.db` gitignore'da). Docker: `/data/matches.db` volume'da (6. adım). |
| Saklama süresi | `Application/Matches/PruneMatches.cs`, `Api/Background/MatchRetentionService.cs`, `MATCH_RETENTION_DAYS` | Varsayılan 180 gün, 0 = hiç silme (karar 2026-11-12). Açılışta bir, sonra günde bir. Silinen maçın linki 404. Geçersiz değer (negatif, sayı değil) uygulamayı açmaz. |
| Lobi ve seri | — | Değişmedi: aktif lobiler bellekte (§8.11). Seri sonucu `games` içinde maç kimliğiyle lobi yaşadıkça durur; lobi kapanınca sadece replay'ler kalır. |

Testler (`MatchStorageTests`, her test kendi geçici dosyasıyla): yeniden başlatmadan sonra aynen okunur (seed üst sınırı dahil) · belge sıkıştırılmış yazılır · aynı kimlik ikinci kez fırlatmaz · 180 günden eskiler silinir, yenisi kalır · 0 gün hiç silmez · API yeniden başlasa da GET kayıtlı baytları döner · üretimde yol zorunlu, geliştirmede bellek · saklama ayarı geçerli/geçersiz değerler. `DraftHubTests`: canlı `MatchReady` ile GET birebir aynı (`DeepEquals`).

Kalan: rejoin (ertelendi). Yedekleme (SQLite `.backup` cron'u) Docker adımında (§8.8).

### 8.17 Uygulama: 5a (ön yüz iskeleti, şampiyon listesi, gateway'de istemci)

**Bir sayfanın yolu:** tarayıcı `/l/{kod}` ister → gateway: yönlendirme hiçbir YARP rotası bulmaz, yol bir istemci yolu → `index.html` (no-cache) → React Router `/l/:code` → `sessionStorage`'da bu lobinin token'ı varsa `GET /api/lobbies/{kod}`, yoksa "Katıl" (`POST /join`). Geliştirmede sayfayı Vite (5173) sunar, `/api` ve `/hubs` gateway'e (5000) proxy'lenir.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Şampiyon listesi | `Application/Matches/ListChampions.cs`, `Api/Champions/ChampionEndpoints.cs` | `GET /api/champions`: `id`, `name`, `roles`, `offRole` (yuva → ceza). `champions.json` istemciye gömülmez: içinde gizli motor sayıları var (§0). Liste bir kez hesaplanır, `Cache-Control: public, max-age=300`; gateway'de token ve özel limit yok (genel IP limiti). |
| Rol dışı ceza | `Engine/MatchEngine.OffRolePenalty` | Tablo motorda tek kaynak (`OffRole`, internal); istemciye hesaplanmış değer gider, kopya tablo yok. Yeni public tip eklenmedi (mimari listesi aynı). |
| İstemci dosyaları | `Gateway/WebClient.cs` | `wwwroot`'tan statik dosya; yönlendirmeden ve rate limiter'dan **sonra** (her dosya isteği genel IP limitinden geçer), JWT'den önce. Önbellek: `index.html` no-cache, `/assets/*` (içerik hash'li) bir yıl `immutable`, diğerleri (portreler) bir gün. |
| SPA yönlendirmesi | `WebClient.IsClientRoute` | Rota eşleşmediyse ve yol GET/HEAD, uzantısız, `/api` ve `/hubs` dışı ise `index.html`. Catch-all fallback **rotası** denendi, reddedildi: aday rota olarak 405'i bozuyordu (`GET /api/lobbies` 404 oluyordu). Beyaz liste korunur: listede olmayan `/api/...` 404 kalır, HTML dönmez; olmayan `app.js` da 404 (HTML'i script diye yüklemez). |
| Şampiyon portreleri | `web/scripts/fetch-champion-icons.mjs` → `web/public/champions/{id}.png` | Data Dragon'dan bir kez (`npm run icons`), repoda (40 × ~30 KB). Kimlik ↔ Riot anahtarı ada göre eşlenir; eşleşmeyen varsa betik durur. Kaynak sürüm `SOURCE.txt`'de (şu an 16.22.1). Sayfa altında Riot'un fan projesi notu (`Layout.tsx`). Karar §1. |
| İstemci yığını | `web/` | Vite 8 + React 19 + TypeScript 7, `react-router-dom`, `@microsoft/signalr` (5b). Stil düz CSS (`index.css`); Tailwind sonradan eklenebilir, mevcut stili bozmaz. Build satır içi script üretmez: CSP `script-src 'self'` değişmedi. |
| Token | `web/src/session.ts` | Lobi koduna göre `sessionStorage`: yenilenen sekme aynı koltuğa döner, sekme kapanınca silinir (localStorage'da iki sekme aynı koltuğu açardı). 401/404'te silinir. Rejoin hâlâ yok. |
| Hata gösterimi | `web/src/api/http.ts` | Gövde `{error, message}` ise sunucunun Türkçe mesajı; gövdesizse (gateway 429) duruma göre Türkçe metin. |
| Geliştirme | `web/vite.config.ts`, `src/LolDraft.Api/appsettings.json` | Proxy gateway'e (API sır başlığı olmadan istek kabul etmez). EF'in SQL komut logları `Warning`'e çekildi (her tikte gürültü yapıyordu). |
| Windows tuzağı | `web/scripts/vitest.mjs` | Terminal küçük harfli sürücüyle (`c:\`, VS Code varsayılanı) açılınca Vitest kendi modülünü `c:/`, testleri `C:/` yolundan yükler; testler "reading 'config'" ile düşer. `npm test` Vitest'i büyük harfli dizinle başlatır. |

Çalıştırma (geliştirme): API (`dotnet run --project src/LolDraft.Api`), gateway (`dotnet run --project src/LolDraft.Gateway`), ikisine de `GATEWAY_SECRET` ve `JWT_SIGNING_KEY` (aynı değerler; user-secrets ya da ortam değişkeni); `cd web && npm run dev` → http://localhost:5173. Gateway'in build'i sunduğunu denemek için: `npm run build`, gateway'e `--webroot <tam yol>/web/dist`.

Testler: Api (`ChampionEndpointsTests`: 40 şampiyon, beş yuva, kendi rolünde 0, tablo değerleri, gizli alan yok), Gateway (`WebClientTests`: istemci yolları index.html + no-cache + CSP, hash'li dosya immutable, portre bir gün, olmayan dosya 404, `/api`/`/API`/`/hubs` SPA'ya düşmez ve API'ye ulaşmaz, `/api/champions` tokensiz), web (Vitest: hata çözme, oturum saklama). Elle: gateway + API + Vite ile lobi aç → katıl → ikinci katılım `lobby_full` → token'la durum; portreler ve CSP başlığı.

Kalan (5b): hub istemcisi, canlı lobi, draft ve swap ekranı. Arayüz henüz tarayıcıda gözle denenmedi (curl ile denendi).

### 8.18 Uygulama: 5b (canlı lobi, draft, swap, geliştirme rakibi)

**Bir hamlenin yolu (istemci):** havuzdan şampiyon seç → "Banla/Seç" → `act('Ban', id)` yeni bir `actionId` (UUID) ile `invoke` → sunucu kuralı uygular → hamleyi yapana `{ok, error, message}`, herkese `LobbyUpdated` (tam durum) → `revision` elimdekinden büyükse ekran o durumu çizer. İstemci kural hesaplamaz; havuzda soluk görünenler (ban'lı, seçilmiş, fearless) sadece görüntü, son söz sunucunun.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Bağlantı | `web/src/live/useLobbyConnection.ts` | Sekme başına tek hub bağlantısı; token `accessTokenFactory` → sorgu parametresi (WebSocket başlık gönderemez, §8.4). Bağlanınca sunucu güncel durumu kendiliğinden yollar (`OnConnectedAsync`); 5a'daki GET ve "Yenile" kalktı. |
| Yeniden bağlanma | aynı dosya, `retryPolicy` | İlk 2 dk (hükmen süresi) sık (0, 1, 2, sonra 3 sn), sonra 10 sn'de bir, 10 dk sonra vazgeç. Varsayılan politika ~30 sn'de bırakıyordu: kısa bir kopma seriyi hükmen kaybettirebilirdi. |
| Hamle tekrarı | `act` | Bağlantı tam hamle anında koparsa yeniden bağlanınca **aynı** `actionId` ile bir kez daha (sunucu ikinci kez uygulamaz, §8.12). Bağlantı ayaktayken gelen hata tekrarlanmaz. Rate limit (sn'de 5) hata fırlatmaz, başarısız cevap döner; mesajı gösterilir. |
| Eski durum | `live/revision.ts` | Küçük ya da eşit `revision` atılır (yeniden bağlanınca eski durum gelebilir). |
| Geri sayım | `live/clock.ts`, `components/Countdown.tsx` | Kalan = `deadline − (yerel saat + offset)`, offset her durumda `serverNow − yerel saat`. Yerel saat kaymış olsa da sunucuya göre sayar. Saniyede 4 çizim; sunucudan saniyelik mesaj yok. Ağ gecikmesi ihmal (saniye çözünürlüğü). |
| Draft ekranı | `screens/DraftBoard.tsx`, `live/draft.ts` | Faz, sıra, sayaç, iki takımın 5 ban + 5 yuvası, rol filtreli ve aramalı havuz, seç → onayla (yanlış tıklama hamle değil). Renk koltuktan: `blueTeam === seat` (renkler her maç değişir). Sıra tablosu Domain'le aynı (test: 20 adım, takım başına 5+5). |
| Swap ekranı | `screens/SwapBoard.tsx` | Pick sırası yuva sırası; iki yuvaya tıkla → `SwapRoles`. Rol dışı yuvada "rol dışı −%X · oynak" (değer `/api/champions`'tan, §8.17). Rakibin dizilimi ve kilit durumu görünür (sunucu ikisini de yollar). Kilitle ya da süre dolsun. |
| Koltuklar ve hükmen | `screens/LobbyScreen.tsx` `Seats` | Gerçek bağlantı durumu (hub). Hükmen sayacı Domain'deki koşulla: seri başladı (`games > 0` ya da draft var), oyuncu yok, rakip bağlı → `absentSince + 2 dk`'ya geri sayım. |
| Skor gizliliği | `live/reveal.ts` | Lobi başına "kaç maçın sonucu açıldı" `sessionStorage`'da (yenilemede izlenen maç tekrar gizlenmez). Skor sadece açılmış maçlardan; seri kazananı son maç açılınca (hükmen ise hemen). 5b'de "sonucu göster" düğmesi açar, 5c'de oynatıcının bitişi. |
| Geliştirme rakibi | `web/scripts/rakip.mjs` (`npm run rakip`) | Tek başına test için otomatik oynayan ikinci oyuncu: `-- <link>` katılır, `-- --yeni bo3 [--fearless]` lobi açıp linki yazar. Gerçek istemci gibi gateway'den (ya da `--gateway http://localhost:5173` ile Vite proxy'sinden) geçer; sunucuya özel kod yok. Ban rastgele, pick sıradaki yuvanın rolünden, swap'ta olduğu gibi kilitler. Oyunun bilgisayar rakibi değil (§14 açık soru ayrı). |

Testler: Vitest (18): revision, saat farkı ve kalan süre, draft sırası, renk, sıra, kullanılamayanlar, yuvalar, rol dışı ceza, gizli skor. Uçtan uca (iki `rakip` birbirine karşı): bo3 gateway üzerinden (2 maç, 40 adım, swap, kilit, seri sonu, iki tarafta aynı maç kimlikleri ve skor; replay linkleri 200), bo1 Vite proxy'si üzerinden (WebSocket proxy'si). Reddedilen hamle yok. Arayüzün kendisi tarayıcıda gözle denenmedi.

Kalan (5c): maç oynatıcısı (harita, log, HUD, karne), `/m/{id}` sayfası, oynatma bitince sonucun açılması.
