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
| Token | `web/src/session.ts` | Lobi koduna göre `sessionStorage`: yenilenen sekme aynı koltuğa döner, sekme kapanınca silinir (localStorage'da iki sekme aynı koltuğu açardı). 401/404'te silinir. Rejoin sonradan geldi (§8.24). |
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

### 8.19 Uygulama: 5c (maç oynatıcısı, karne, tekrar izleme sayfası)

**Bir karenin yolu:** `requestAnimationFrame` → oyun saati `T += geçen gerçek süre × hız × 20` (kare başına en fazla 0,1 sn: arka plandaki sekme dönünce sıçramasın) → `stateAt(replay, T)` (skor, ejder, kaybedilen kuleler, ölüler, log, altın farkı) → her şampiyon için `placement(...)` → SVG. Olay "uygulanmaz", T'ye kadar olanlar sayılır: sona atla, baştan izle ve hız değişimi aynı fonksiyondan; mantık React'tan bağımsız ve testli.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Belge tipleri | `web/src/match/types.ts` | `MatchReplay.cs` ile birebir (olay birleşik tipi `type` alanıyla). Kule `side` = yıkan taraf; kaybeden karşı taraf sayılır. |
| Kadro ve roller | `timeline.roster` | Belgede rol yok: `goldFrames[0]` yuva sırasıyla gelir (mavi top…support, sonra kırmızı). Bu sıra sözleşmedir (motor `MatchRun.Gold.RecordGoldFrame`); değişirse oynatıcı rolleri yanlış yerleştirir. |
| Konumlar | `match/positions.ts`, `geometry.ts` | Prototipin kuralları, öncelik sırasıyla: ölü → üs; objektif (−22…+5 sn) → çukur; kuşatma (lead/tail) → savunanın üssü önü; `move` → koridor ön hattı ya da nehir; `split` → rakibin dış kulesi; recall (oyuncu başına kaydırılmış 270 sn periyot) → üs; yoksa rolün yeri (ormancı kamplar, 14. dk'dan sonra bot ikilisi mid, 25. dk'dan sonra top yan koridor). Ön hat v1'in koridor puanı yerine **kaybedilen kulelerle** kayar (belgede koridor puanı yok). Yumuşatma CSS `transition: transform 0.3s`. |
| Nehir | `geometry.ts` | Sol üstten sağ alta (çukurlardan geçer, mid'e dik). Prototip nehri mid'le aynı köşegene çiziyordu. |
| İşaretler | `MapView.Pops` | T'den türetilir (zamanlayıcı yok): olaydan sonraki 25 oyun sn. Kill işareti öldürenin yanında (kurban o anda üsse taşınmış olur). |
| HUD | `MatchPlayer` | Kill, saat, altın farkı (`goldDiffAt`: dakika kareleri arasında doğrusal), ejder, kule. İhtimaller (%) varsayılan gizli (§0), "Matematiği göster" açar. |
| Karne | `match/MatchReport.tsx` | Kazanan (koltuk biliniyorsa "Kazandın/Kaybettin"), dakika, bitiş türü, son savaş ihtimali; iki takımın altın kaynakları; kurallar etkiye göre (ilk 12, çubuk en büyüğe oranla); hiper taşıyıcılar; rol dışı kumarlar. Oynatma bitince açılır. |
| Lobide | `screens/LobbyScreen.tsx` `GameViewer` | Oynanmış ama izlenmemiş ilk maç kendiliğinden açılır (MatchReady'nin belgesi; sayfa yenilendiyse GET). Bitince (ya da sona atla) skor açılır, oynatıcı karneyle açık kalır, "Kapat" → seri paneli ve "Hazırım". Eski maçlar "tekrar izle" (skoru etkilemez) ve paylaşım "link"i. 5b'deki geçici "sonucu göster" kalktı. |
| Paylaşım sayfası | `pages/ReplayPage.tsx` | `/m/{id}`: `useReplay` → GET; koltuk yok, sonuç "Mavi kazandı". |
| Belge kaynağı | `match/useReplay.ts` | Elde varsa (MatchReady) onu, yoksa bir kez GET (belge dondurulmuş, değişmez). |

Testler (Vitest, 31): gerçek bir maç belgesi (`match/__fixtures__/match.json`, iki geliştirme rakibinin oynadığı) üzerinden zaman çizelgesi (başta sıfır, sonda belgedeki kill sayısı, log sırası ve süzgeci, ölü → yeniden doğma, kule kaybeden tarafa, altın aradeğeri, kadro), konumlar (ölü üste, objektifte yaşayanlar çukura 60 px içinde, ön hat kayması), çizim duman testleri (`renderToString`: 10 şampiyon, 18 kule, sonda yıkık kule sayısı, karne koltuğa göre). Arayüz tarayıcıda gözle denenmedi.

Kalan: 6. adım (Docker + CI). Oynatıcıda geri sarma çubuğu ve gerçek hareket tablosu (`transitions.json`, v0.2) yok.

### 8.20 Uygulama: 6 (Docker, Caddy, SQLite yedeği, CI)

**Bir isteğin yolu (konteynerde):** tarayıcı `https://alan-adı/l/{kod}` → **caddy** (443, TLS'i çözer; `X-Forwarded-For` = bağlanan IP, istemcinin gönderdiğini yok sayar; `X-Forwarded-Proto: https`) → `edge` ağında **gateway:8080** (Caddy'nin sabit IP'si `172.28.0.10` güvenilen proxy: istemci IP'si ve şema başlıktan alınır; rate limit, JWT, güvenlik başlıkları, `wwwroot`) → `internal` ağında **api:8080** (paylaşılan sır) → `matches` volume'unda `/data/matches.db`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| İmajlar | `src/LolDraft.Api/Dockerfile`, `src/LolDraft.Gateway/Dockerfile` | Çok aşamalı: SDK'da derle, `aspnet:10.0`'da çalıştır (SDK, kaynak kod, Node imaja girmez). Önce sadece `.csproj`/`package*.json` kopyalanır: kod değişince restore/`npm ci` katmanı önbellekten gelir. Gateway'de üçüncü aşama: `web/` Node'da derlenir, `dist` → `/app/wwwroot` (§8.17). Bağlam repo kökü; `.dockerignore` sırları (`.env`), veritabanlarını, `bin/obj/node_modules`'ı ve oyunla ilgisiz klasörleri dışarıda tutar (`data/`'dan sadece `champions.json`). |
| Kullanıcı | Dockerfile'lar, `deploy/backup/Dockerfile` | Root değil: aspnet imajının `app` kullanıcısı (1654). Yedekçi de 1654 (WAL'da okuyucu da `-shm`'e yazar). `/data` iki imajda da 1654'ün: boş volume ilk bağlandığı konteynerdeki klasörün sahibini devralır, hangisi önce bağlarsa bağlasın doğru olur. |
| Sertleştirme | `docker-compose.yml` | Hepsinde `cap_drop: ALL`, `no-new-privileges`; .NET ve yedekçi `read_only` kök dosya sistemi (+ `/tmp` tmpfs). Caddy'ye `NET_BIND_SERVICE` geri verilir: ikilisi bu yetkiyle işaretli, bounding set'ten düşerse hiç başlamaz. |
| TLS | `deploy/Caddyfile` | Karar §1. `SITE_ADDRESS=localhost`: Caddy kendi iç CA'sıyla sertifika üretir; gerçek alan adında Let's Encrypt'ten alır ve yeniler (`caddy-data` volume'u silinmesin: sınırlara takılır). `Server` ve `Via` başlıkları silinir; `encode zstd gzip` (gateway sıkıştırmıyordu). WebSocket ek ayarsız geçer. |
| Güvenilen proxy | compose `Gateway__TrustedProxies__0` | Docker IP'leri her açılışta değişebilir; `edge` ağı sabit alt ağlı (`172.28.0.0/24`), Caddy sabit IP'li. Gateway kodu değişmedi (§8.14'teki "liste boşsa hiçbirine güvenme" kuralı aynı). Doğrulama: HSTS debug logu "localhost hariç" diyor; bu kontrol `IsHttps`'ten sonra gelir, yani `X-Forwarded-Proto` kabul edildi. Not: ASP.NET HSTS'i `localhost`'a hiç yollamaz (`ExcludedHosts`), gerçek alan adında yollar. |
| Host listesi | compose `AllowedHosts: ${SITE_ADDRESS}` | Production'da `*` ile gateway açılmaz (§8.10). Bilinmeyen SNI'ya Caddy TLS el sıkışmasını yapmaz; Host'u uymayan istek gateway'e ulaşmaz. |
| Ağlar | compose `networks` | `edge` (caddy, gateway), `internal: true` (gateway, api): api'nin dışarıya portu yok, internete çıkışı da yok (bir bağımlılık dışarı bağlanmaya çalışsa bile). Yedekçi `network_mode: none`. |
| Yedek | `deploy/backup/backup.sh` | `sqlite3 .backup` (SQLite'ın yedek API'si; `cp` yazma anına denk gelirse ya da veri WAL'daysa yarım kopya verir). Geçici ada yazılır, `PRAGMA integrity_check` = ok ise asıl adını alır; en yeni `BACKUP_KEEP` kalır. Açılışta bir kez, sonra `BACKUP_INTERVAL_HOURS`'ta bir (cron yerine `sleep` döngüsü: root olmadan çalışır). Elle: `docker compose run --rm backup once`. Yedekler aynı makinede: disk giderse gider; makine dışına kopyalama sunucu kurulurken (açık iş). |
| Geri yükleme | aşağıda | `docker compose stop api` → `docker compose run --rm --no-deps --entrypoint sh backup -c 'rm -f /data/matches.db-wal /data/matches.db-shm && cp /backups/matches-<zaman>.db /data/matches.db'` → `docker compose start api`. Eski `-wal`/`-shm` silinmezse SQLite onları yeni dosyaya uygulamaya çalışır. |
| Sırlar | `.env.example` → `.env` | Compose `${GATEWAY_SECRET:?…}` ile eksikse hiç başlamaz; uygulamalar da zayıfsa açılmaz (§8.6). `.env` gitignore'da (`!.env.example` istisnası) ve `.dockerignore`'da. |
| Satır sonu | `.gitattributes` | `*.sh` ve `Caddyfile` LF: bu makinede `core.autocrlf=true`; CRLF'li betik konteynerde `/bin/sh^M` ile başlamaz. |
| CI | `.github/workflows/ci.yml` | Push (main) ve PR: .NET restore/build/test (SDK `global.json`'dan), web `npm ci` + lint + test + build, ikisi geçerse üç imajın derlemesi. İmaj bir yere gönderilmez; sır gerekmez. Veri hattı CI'da yok (Riot anahtarı ister). |
| Bilinen gürültü | api/gateway logu | "DataProtection: ephemeral key repository" uyarısı: kimlik doğrulama paketi DataProtection'ı kaydeder ama hiçbir şey onunla şifrelemiyor (cookie yok, token JWT). Zararsız; bir gün cookie gelirse anahtarlar volume'a alınmalı. |

Çalıştırma: `cp .env.example .env` (iki sırrı `openssl rand -base64 48` ile doldur) → `docker compose up -d --build` → https://localhost (ilk seferde tarayıcı iç CA'yı tanımaz; uyarıyı geç ya da `docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt .` ile kökü güvenilenlere ekle). Geliştirme rakibi: `NODE_EXTRA_CA_CERTS=<root.crt> npm run rakip -- --yeni bo1 --gateway https://localhost`. Windows curl'ü (schannel) iç CA'da iptal listesi arar: `--ssl-no-revoke`.

Doğrulama (elle, 2026-11-13): üç imaj derlendi, dört konteyner ayakta; HTTP → 308 HTTPS; `/l/x` index.html + CSP + `no-cache`, `/assets/*.js` `immutable` + zstd, portre 200, olmayan dosya 404, beyaz liste dışı `/api/...` 404, tokensiz lobi 401, `Server`/`Via` yok; iç ağdan sırsız istek API'de 404; iç ağdan internete çıkış yok. İki geliştirme rakibi Caddy'nin TLS'i üzerinden bo1 oynadı (REST + WebSocket). `compose down` + `up` sonrası replay birebir aynı. Yedek: açılışta ve elle alındı, integrity ok, maç içinde; geri yükleme provası (veritabanı silinince 404, yedekten sonra 200); rotasyon (`BACKUP_KEEP=1`). CI adımları Linux konteynerlerinde: .NET 408 test, web lint + 31 test + build geçti. GitHub'da henüz koşmadı (ilk push'ta).

Kalan: sunucuya kurulum (alan adı, `SITE_ADDRESS`, yedeklerin makine dışına kopyası), rejoin, tarayıcıda gözle deneme (harita ve hareket değişecek, sonraya).

### 8.21 Uygulama: H4 (80'lik havuzun ürün tarafı)

H1-H3 havuzu ve motoru 80 şampiyona taşıdı (DENGE.md faz H); H4 bunu oyuncuya ulaştırır.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Portreler | `web/public/champions/`, `npm run icons` | 40 yeni portre, Data Dragon 16.22.1. Eski 40'la aynı yama: betik hepsini yeniden indirdi ama eski dosyalar bayt bayt aynı, git'te sadece 40 yeni dosya. Kontrol: `champions.json`'daki 80 kimliğin hepsinin portresi var, fazla dosya yok. Yeni şampiyon eklenince aynı komut (eşleşmeyen ad olursa betik durur). |
| Fearless bo5 | `web/src/pages/HomePage.tsx` | Ön yüzdeki `fearlessBlocked` kilidi ve "havuz yetmiyor" notu kalktı; her format fearless'la açılır. Sunucu değişmedi: `Series.Start` havuz kontrolü boyuttan hesaplıyor (80 − 4 × 10 = 40 ≥ 20, §8.3), `LobbyEndpointsTests` bo5 + fearless'ı zaten kabul ediyordu. Havuz bir gün küçülürse ön yüz seçtirir, sunucu `pool_too_small` ile reddeder; hata mesajı sayfada görünür. |
| K13 kombo | `src/LolDraft.Engine/Simulation/TeamRules.cs` | Tablo 7 → 15 ikili (liste ve ölçüm DENGE.md faz H4). `Combos` `private` → `internal`: test tabloyu havuzla karşılaştırabilsin. |
| Kombo testi | `TeamRulesTests.Kombo_tablosundaki_her_sampiyon_havuzda_var` | Kombo kimlikle eşleşir; yazım hatalı bir kimlik (ör. `"kaisa"` yerine `"kai'sa"`) hiç tetiklenmez ve kimse fark etmez. Test repo kökünü yukarı yürüyerek bulur, `data/champions.json`'daki kimlikleri okur; tablodaki her kimlik havuzda olmalı, ikili tekrarlanmamalı. |

Doğrulama: .NET Domain 102, Engine 186 (+1 kombo testi), Api 82, Gateway 50; web lint + 31 test + build. Denge: 20.000 draft önce/sonra, genel göstergeler değişmedi (DENGE.md faz H4).

Kalan: havuzun geri kalanı (~90), aynı yol (H1-H4).

### 8.22 Uygulama: havuz parti 1 (80 → 125)

Yol H1-H4'le aynı, sırası değişti: taslak önce **geçici havuz dosyasında** ölçülür (`--champions <dosya>`), onaydan sonra `data/champions.json`'a yazılır. Böylece kullanıcı etiketleri ölçüm sonuçlarıyla birlikte görür, onaylanmamış veri repoya girmez.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Taslak | geçici betik (repoya girmez) | 45 şampiyon, her biri §16.2 biçiminde; havuzda olanı tekrar eklerse durur. Geçici havuz = 80 + 45 → `--check` ve 30.000 draft. Ölçümle 3 düzeltme ve kullanıcının 3 değişikliği (DENGE.md parti 1). |
| Havuza yazma | `data/champions.json` | Yeni kayıtlar kendi koridor grubunun sonuna, dosyanın 3 satırlık kompakt biçiminde; dosya yeniden biçimlenmez. Diff'te silinen sadece not satırı ve Senna'nın satırı (sona virgül). Yazılan 125 kayıt taslakla birebir karşılaştırıldı. |
| Portreler | `web/public/champions/` | `npm run icons`: 45 yeni, eski 80 aynı yama (16.22.1) ve bayt bayt aynı; 125 kimlik = 125 portre. |
| K13 | `TeamRules.cs` | +2 ikili (Lucian + Nami, Kog'Maw + Lulu) → 17. Havuz eşleşme testi (§8.21) yenileri de denetler. |
| Sayıya bağlı test | `ChampionEndpointsTests` | 80 → 125 (test adı da). Lobi testi ve HomePage'deki havuz hesabı yorumları 125'e göre (40 kilit, 85 kalır). |

Doğrulama: .NET Domain 102, Engine 186, Api 82, Gateway 50; web lint + 31 test + build; `--check` 2 bilinçli uyarı (Kayle, Nilah).

Kalan: parti 2 (~45: Locke, Zaahen, Yunara için kit bilgisi kullanıcıdan; Ambessa, Pantheon, Zoe, Hwei, Aurora, Ryze, Rek'Sai…).

### 8.23 Uygulama: havuz parti 2 (125 → 170)

Yol §8.22'yle aynı (taslak geçici havuzda ölçülür, onaydan sonra yazılır). Farklar:

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Kapsam | `data/champions.json` | Kalan 45'in hepsi; Locke, Zaahen, Yunara dışarıda (kullanıcı kararı, DESIGN.md §1). Not satırı artık "gerçek oyundakilerin hepsi, üçü hariç" diyor. Bu partide adc grubuna kayıt eklenmedi (saf nişancı kalmadı), yazma betiği boş grubu atlar. |
| Ölçüm hassasiyeti | denge aracı | 170'lik havuzda 30.000 draft şampiyon başına ±2,0 veriyordu: bandın kenarındaki %55,3'ler gürültüden ayrılmıyor. Ölçüm 60.000 draft'a çıktı (±1,0-1,4). |
| Portreler | `npm run icons` | İlk çalıştırma ağ hatasıyla yarıda kesildi (yeni dosya yazılmadı, eskilere dokunulmadı); ikinci çalıştırma 170'in hepsini indirdi. Kontrol: 170 kimlik = 170 portre, eskiler bayt bayt aynı, şüpheli küçük dosya yok. Betik zaten "eşleşmeyen olursa dur" diye yazılı; ağ hatasında da durur, yarım dosya bırakmaz. |
| K13 | `TeamRules.cs` | +1 ikili (Zeri + Yuumi) → 18. |
| Sayıya bağlı test ve yorumlar | `ChampionEndpointsTests`, `LobbyEndpointsTests`, `HomePage.tsx` | 125 → 170 (bo5 fearless: 40 kilit, 130 kalır). |

Doğrulama: .NET Domain 102, Engine 186, Api 82, Gateway 50; web lint + 31 test + build; `--check` 2 bilinçli uyarı (Kayle, Nilah). Denge DENGE.md parti 2.

Kalan: Locke, Zaahen, Yunara (kit bilgisiyle); bant dışındakiler için denge turu (DENGE.md parti 2 sonu).

### 8.24 Uygulama: koltuğa geri dönme (rejoin) ve 2 saatlik lobi ömrü

**Sorun:** token sekmeye bağlı (`sessionStorage`). Sekmeyi kapatıp linki yeniden açan oyuncu "Katıl" görüyordu, katılmak `lobby_full` alıyordu ve 2 dk sonra seri hükmen gidiyordu.

**Akış (adım adım):**
1. Lobi açılınca ya da katılınca bilet bir koltuk anahtarı taşır: `{code, seat, token, rejoinToken}`. Anahtar 32 bayt CSPRNG, base64url 43 karakter. Lobi sadece SHA-256 özetini tutar (`RejoinKey`).
2. İstemci token'ı `sessionStorage`'a, anahtarı yazıldığı anla birlikte `localStorage`'a yazar.
3. Oyuncu linki yeniden açar: sekmede token yok, anahtar var → `POST /api/lobbies/{code}/rejoin { rejoinToken }`.
4. Lobinin kilidi altında özet eşleşir → anahtar yenisiyle değişir → aynı `PlayerId` ve koltuk için yeni JWT + yeni anahtar. İstemci ikisini yazar, hub'a bağlanır (bağlanınca "yok" sayacı durur).

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Lobi ömrü | `Domain/Lobby.cs` (`MaxLifetime`, `CreatedAt`) | Açılıştan 2 saat sonra kapanır, hamle sayacı sıfırlamaz (hareketsizlik kapanışından farkı). Mevcut kapanış yolu: depodan silinir, `LobbyClosed`. Oynanmış maçlar SQLite'ta, `/m/{id}` çalışır. JWT de 2 saat: lobi içinde hiç dolmaz, token yenileme gerekmez. Kapanış metni sebebi söyler. |
| Anahtar özeti | `Domain/RejoinKey.cs` | Domain kriptografi bilmez: opak, karşılaştırılan değer. `ToString` özeti yazmaz (log/hata mesajı). |
| Anahtarın verilmesi | `Lobby.AssignRejoinKey` | `Create`/`Join` imzası değişmedi (19 test çağrısı); Application oturtmayla aynı kilit içinde, hemen ardından çağırır. Açan için depoya eklenmeden önce. Yeni anahtar eskisini geçersiz kılar. |
| Eşleşme | `Lobby.Rejoin` | Tek kullanımlık: kullanılan anahtar yenisiyle değişir. Görünür durum değişmez (sürüm artmaz), hareketsizlik sayacı sıfırlanır. Yanlış anahtar hiçbir şeyi değiştirmez. |
| Üretim ve özet | `Application/Lobbies/RejoinSecrets.cs` | SHA-256, tuzsuz ve hızlı: anahtar rastgele 256 bit, sözlük ya da kaba kuvvet yok. Biçimi bozuk anahtar (43 karakter base64url değil) özetlenmeden reddedilir. |
| Uç nokta | `Api/Lobbies/LobbyEndpoints.cs` | Token istemez (token'ı kaybeden içindir). Anahtar gövdede (URL'de olsa loglara düşerdi). Hata: yanlış, kullanılmış, başka lobinin ya da bozuk anahtar 403 `rejoin_rejected`; lobi yok 404. Ayrı kod bilgi sızdırmaz: lobi kodu zaten gizli, kodu bilen "katıl" ile varlığını öğrenebilir. |
| Gateway | `appsettings.json` `lobby-rejoin` | Rota önceden hazırdı: JWT istemez, katılmayla aynı hız sınırı (dakikada 10). Gövde loglanmaz. |
| İstemci | `web/src/session.ts`, `pages/LobbyPage.tsx` | Token yok + anahtar var → kendiliğinden geri dönüş ("Koltuğuna dönülüyor…"). StrictMode efekti iki kez çalıştırır: ikinci istek kullanılmış anahtarı yollar ve anahtar silinirdi; `ref` ile bir kez. Sunucu reddederse (`rejoin_rejected`, `lobby_not_found`) anahtar silinir, "Katıl"a döner; ağ hatasında anahtar kalır, düğme "Koltuğuna dön" (tekrar dene). 2 saatten eski anahtar okunmaz ve her kayıtta temizlenir (localStorage'da birikmez). Lobi kapanınca ikisi de silinir. |
| Eski token | — | Geri dönüşte eski JWT süresi dolana kadar geçerli (karar): iki sekme aynı koltukta zaten destekleniyor (bağlantı sayımı). |

Doğrulama: Domain 110 (+1 ömür, +7 rejoin), Api 92 (+10: anahtar biçimi ve ayrılığı, aynı oyuncu/koltuk, eski token geçerli, tek kullanımlık, başka lobinin/uydurma/bozuk/boş anahtar, olmayan lobi), Gateway 51 (+1 uçtan uca: tokensiz rejoin, yeni token gateway'den geçer), Engine 186; web 35 (+4: sekme kapanınca kalır, yenisi yazılır, 2 saat, bozuk kayıt) + lint + build. Tarayıcıda elle deneme yapılmadı.

### 8.25 Uygulama: maç ekranında gerçek harita

**Kavram:** kendi çizdiğimiz düz harita (çapraz nehir, köşeli koridorlar) yerine Summoner's Rift görseli. Görsel tek başına yetmez: kuleler, çukurlar, koridor yolları ve kamplar görseldeki gerçek yerlerine oturmalı, yoksa ikonlar duvarın içinde yürür.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Görsel | `web/scripts/fetch-champion-icons.mjs` → `web/public/map/map11.png` | Data Dragon `img/map/map11.png` (512×512), portrelerle aynı yamadan, aynı komutla (`npm run icons`); kaynak `SOURCE.txt`. Tarayıcı Riot'a gitmez, CSP değişmez (`img-src 'self'`); gateway diğer statik dosyalar gibi bir gün önbellekle sunar. Bu yamada görsel sadeleştirilmiş, düz renkli minimap (bej koridor, mavi nehir, siyah duvar): ikonlar için okunur zemin. SVG'de hafif karartılır (`brightness 0.8`). |
| Koordinat | `web/src/match/geometry.ts` `riot(x, y)` | Görselin kapsadığı Riot alanı x -120…14870, y -120…14980 (zone-painter'la aynı; y ters). |
| Kuleler | `TOWERS` | Elle tahmin değil veri: 300 timeline'daki `BUILDING_KILL` konumları; her kule her maçta birebir aynı noktada. Gerçekte kule koridorun kenarında durur; kullanıcı isteğiyle şeridin ortasına alındı: görselde koridora dik taranıp yol renkli şeridin (koridor `(145,154,111)`, üs içi yol `(115,150,159)`) orta noktası bulundu (şerit ~38 birim; kayma 1-15). Koridor boyunca yer aynı. Mid dış kuleler nehir kavşağında (şerit ~70, geniş): mid'in orta çizgisine (x + y = 600) izdüşürüldü. |
| İnhibitörler | `INHIBITORS` | Üs kulesinin arkasında; konum aynı yoldan (veri + şerit ortası). Motorda üs kulesiyle inhibitör tek kademe (tier 2; log "…inhibitörünü yıktı"): kademe düşünce ikisi birlikte yıkık. Ayrı durumu olmadığı halde gösterilmesi bu yüzden "ekrandaki her şeyin motorda karşılığı var" ilkesine uyar. |
| Çukurlar, nehir, kamplar | `PIT`, `RIVER_SPOT`, `CAMPS` | Çukurlar `ELITE_MONSTER_KILL` medyanı (ejder 1047, Baron 304 olay); Herald ve Baron aynı çukur. Nehir noktaları mid ile çukur arası. Kamplar dört tane (mavi buff, kurtlar, kuşlar, kırmızı buff), Riot koordinatından. |
| Koridor yolları | `LANE_PATH` | Şeridin ortasından, kulelerin ve inhibitörlerin içinden geçer (şampiyonlar da ortada yürür). Top/bot köşesi keskin L değil, görseldeki yuvarlatılmış yay: koridor piksellerinde mesafe dönüşümü (pikselin kenara uzaklığı), köşeden çıkan ışınlar boyunca en uzak nokta = şeridin ortası; her köşede 9 nokta. İlk keskin L (x 50 / y 48 kesişimi) lane fazındaki şampiyonları şeridin dış kenarına itiyordu (lane fazı köşe civarında geçer; s = 0,5 yayın tepesi). Ölçüm: top/bot koridorundaki 1247 konum şerit ortasının ortalama %84'ünde, en kötü %76'sında (≤ 5 birim). |
| Kule yeri (s) | `TOWER_S` | Koridor başına (eskiden iki taraf için tek dizi); kule noktasının yola izdüşümünden hesaplanır. Dış kuleler 0,38-0,39 (eskiden sabit 0,38): ön hat mantığı (`laneFront`) aynı kaldı. Split konumu tek değişen kullanım (`TOWER_S[taraf][koridor][0]`). |
| Koridor içi kaydırmalar | `positions.ts` | Hareket olayında `(+6, +6)` ve split'e cevap verende `(+10, +10)` çapraz kaydırma vardı: düz haritada zararsız, top/bot'ta şampiyonu şeridin kenarına itiyordu. Artık koridor boyunca (s'de) kaydırılır. Mid'deki bot/destek yan yana kaydırması (`±9, ±9`) mid'e dik, şeridin içinde: kaldı. |
| Çizim | `MapView.tsx`, `index.css` | Zemin, nehir, koridor çizgileri ve çukur etiketleri kalktı (görselde var). Görsel yüklenemezse koyu zemin kalır. |
| Kule, inhibitör ve nexus ikonu | `web/public/map/tower.png`, `inhibitor.png`, `nexus.png`; `MapView` `TINT` | Oyunun minimap ikonları (64×64, gri). Data Dragon'da yok: CommunityDragon `game/assets/ux/minimap/icons`, Data Dragon sürümünden türetilen yama klasörüne sabit (16.22.1 → `16.22`; `latest` her yamada değişir). Takım rengi SVG renk matrisiyle (gri × takım rengi; siyah kenar korunur, renkler 1'in üstünde çarpanla açılır). Boyutlar (harita birimi): kule 26 (şampiyon ikonuyla aynı; ilk 20 küçük kaldı), inhibitör 22, nexus 34 üs noktasında; yıkılan kule ve inhibitör %25 opak (`map-tower down`, `map-inhibitor down`; render testi ikisini ayrı sayar). Not: bu makinede Windows curl'ü (schannel) CommunityDragon'da TLS hatası verir; betik Node `fetch` kullanır, etkilenmez. |

Doğrulama: noktalar görselin üstüne çizilip gözle kontrol edildi (çukurlar çukurda, kuleler koridorda, kamplar açıklıkta); web 35 test + lint + build. Örnek maç geliştirme rakipleriyle oynatıldı (`/m/{id}`).

### 8.26 Uygulama: şampiyonlar yürür (hareket katmanı)

**Sorun:** şampiyonlar kavgalara ışınlanıyordu. Konum her an `placement()`'tan "şu an nerede olmalı" diye anında geliyordu (ör. ejderden 22 sn önce herkes çukurda). Tek yumuşatma 0,3 sn'lik CSS geçişiydi, 1x'te 20 oyun sn/gerçek sn akarken bu ~6 oyun sn'de haritayı yarılamak demekti; geçiş de düz çizgideydi, duvarların içinden.

**Kavram:** `placement()` artık **hedef**; şampiyon hedefine haritada, duvardan geçmeyen yoldan, oyun hızında **yürür**. Güzergâh maç başına bir kez hesaplanır; ekrandaki konum o güzergâhın T anındaki noktası (saf: geri sarma, hız değişimi bedava).

```
placement() her saniye  ──▶  duraklar (aynı hedef sürdükçe tek durak: [başlangıç, bitiş], nokta, tür, en geç varış)
                                  │
                                  ▼
                  durak durak yürüyüş planı (nav.ts: ızgarada yol, 13,8 birim/sn)
                  olay durağı: başlangıç − yol süresi'nde yola çık; yetişemezse ≤ 2 kat hızlan
                                  │
                                  ▼
                  poseAt(güzergâh, T) → nokta, ölü mü (+ beklerken hafif kıpırdanma)
```

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Yürünebilir ızgara | `web/scripts/build-navgrid.mjs` (`npm run navgrid`) → `web/src/match/navgrid.json` | Görselde duvar siyah, gerisi (koridor, orman yolu, nehir, üs) yürünebilir: parlaklık eşiği (en büyük kanal > 30). 5 birimlik hücre, 120×120 (koridor ~7-8 hücre); yürünebilir %65,7. PNG bağımlılıksız çözülür (Node `zlib`). Satırlar `.`/`#` metin: diff'te okunur. Görsel değişirse betik yeniden koşar. |
| Yol bulma | `web/src/match/nav.ts` `route()` | İki nokta aynı koridorun orta çizgisine ≤ 8 birimse yol koridorun kendisi (`laneSlice`: şeridin tam ortası, köşede yay). Değilse A*: adım bedeli uzunluk × (1 + 2 / duvara uzaklık) (mesafe dönüşümü; duvar dibi ortadan ~2 kat pahalı → yol ortadan geçer), çapraz adım iki duvar arasından kayamaz, tahmin kuş uçuşu × 1,5 (aramayı daraltır; ilk hesap 444 → ~300 ms). Sonra Chaikin yumuşatma + Douglas-Peucker. Hücre çiftine göre önbellek (kamp turları, üsse dönüşler tekrar eder). |
| Durak noktası | `settle()` | Hedef noktaların bir kısmı (çukur çevresi dizilim, kuşatma noktası) duvara düşüyordu (örneklerin ~%2'si): durak, duvara en az 2 hücre uzak en yakın yere oturur. Beklerken kıpırdanma da böylece duvara taşmaz. |
| Hız | `movement.ts` `WALK_SPEED`, `MAX_BOOST` | LoL ~345 birim/sn → 13,8 harita birimi/oyun sn (üsten top dış kulesine ~30 sn). Olaya yetişemeyen en fazla 2 kat hızlanır (kullanıcı kararı; koşma/hayalet/iksir yerine). Örnek maçta objektif anında yaşayan 90/90 şampiyon çukurda. |
| Zamanında yola çıkma | `buildTrack` | Olay durağı (`kind: 'event'`, `deadline` = olayın saniyesi) için çıkış = durak başlangıcı − yol süresi: şampiyon kendi koridorundan erken ayrılır, nehirden gelir (rotasyon). Boşta durağına (koridor, kamp) başlangıcında çıkılır; yolda yeni hedef gelirse yürüyüş orada kesilip yeni hedefe dönülür. Olay sürerken (`floor`) ayrılmaz. |
| Ölüm | | Öldüğü yerde silik kalır (eskiden üsse ışınlanırdı); yeniden doğunca üste belirir, yürüyerek döner. |
| Recall | `RECALL_CHANNEL` | 8 sn yerinde kanal, sonra üsse ışınlanır (oyundaki gibi), sonra yürür. Ölüm recall'u keser. Güzergâhta tek "sıçrama" türü: recall ve doğma, ikisi de kendi üssüne (test). |
| Beklerken | `poseAt`, `IDLE_SWAY` | Hedefte dikilen ikon cansız duruyordu: 3,5 birimlik yavaş salınım, şampiyona göre evreli; varınca ve çıkmadan 3 sn önce söner (yürüyüşe atlamaz). |
| Hedef türleri | `positions.ts` `PlacementKind`, `deadline` | `placement()` mantığı aynı (öncelik sırası, pencereler); sadece her hedefe tür (`dead`, `event`, `recall`, `idle`) ve olaysa en geç varış eklendi. |
| Çizim | `MatchPlayer.tsx`, `MapView.tsx`, `index.css` | Güzergâhlar `useMemo` ile maç başına bir kez (~0,2-0,3 sn, maç açılırken); her karede `poseAt`. CSS `transform` geçişi kalktı (konum zaten her karede doğru; geçiş ikonu geride bırakırdı), sadece ölünce solma. |
| Karar anlarıyla uyum | | Güzergâh sadece bilinen olaylardan kurulur, hareket katmanı olay türlerini bilmez (sadece hedef + tür + en geç varış). Maç içi karar anları gelince (DESIGN §4.10) yeni olaylar eklenir, güzergâh yeniden kurulur; motorun sonucu karardan en az yürüme süresi kadar sonraya koyması yeter. |

Doğrulama: `movement.test.ts`: yol boyunca her birim yürünebilir; maç boyunca hiçbir an duvarda değil; ardışık 0,5 sn arası koşma hızını aşan tek sıçrama kendi üssüne; objektif anında yaşayanlar çukurda; ölen öldüğü yerde, doğunca üste; saflık. İki maçta güzergâhlar haritaya çizilip gözle kontrol edildi (orman yolları, nehirden rotasyon, koridor ortası). Web 41 test + tsc + lint + build.

### 8.27 Uygulama: draft ekranı (turnuva yayını görünümü)

**Kavram:** draft ekranı çalışıyordu ama bir bakışta okunmuyordu: kimin sırası, ne kadar süre kaldı, ne seçildi. Yayın gibi: üstte süre ve sıra, iki yanda takımlar büyük kartlarla, seçim anı görünür bir an. Sadece görünüm; mantık (seçim, onay, hata, kurallar sunucuda) aynı, sunucu değişmedi.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Üst bant | `DraftBoard.tsx` `StepTimer`, `.draft-head` | Takım adları iki yanda (sıradaki parlak, diğeri soluk), ortada evre + "Sıra sende: PICK" + büyük sayaç. Altında sıradakinin renginde boşalan süre çubuğu (adım 30 sn = Domain `DraftTimings.Default`; kırmızı sıradayken sağda durur). Son 5 sn sayaç ve çubuk kırmızı, sayaç nabız. Sayaç 4 kez/sn, çubuk CSS geçişiyle akar. |
| Ban şeridi | `BanStrip` | Bandın altında: mavinin 5 ban'ı solda (mavinin üstünde), kırmızınınki sağda, ayna gibi (ilk ban en sağda); 40 px. İlk sürümde burada 20 adımlık karışık sıra şeridi vardı (mavi/kırmızı, ban/pick iç içe) ve ban'lar takım sütunlarında da duruyordu; kullanıcı isteğiyle şerit sadece ban'lar oldu, sütundaki tekrar kalktı (pick'ler zaten kartlarda). Sıradaki ban yuvası parlar. |
| Pick kartları | `PickCard`; `web/public/champions/splash/{id}.jpg` | Data Dragon yatay "centered" splash (1280×720, temel kostüm; karakter ortada). İlk sürüm dikey yükleme görseliydi (308×560): ~3:1 yatay karttan kesilen şeritte karakterin sadece bir dilimi kalıyordu (Galio'da gövde); centered splash'te karakter bütün. Kartta `object-position: center 30%`, kendi tarafından içeri belirir (maske), alt şeritte rol ikonu + ad. Sıradaki yuva altın nabız, "seçiyor…". Kilitlenince kart kısa bir parlamayla dolar (`pick-in`; React anahtarı değişince animasyon yeniden oynar). |
| Ön izleme | `TeamColumn` `preview` | Sıra bendeyken havuzdan tıkladığım şampiyon onaylamadan kendi sıradaki yuvamda yarı saydam görünür (ban'da ban yuvasında). Sadece benim ekranımda; rakibe göstermek (LoL'deki hover) hub değişikliği ister, kullanıcı kararıyla ayrı adım. |
| Ban'lar | `.ban-slot` | Gri ikon + kırmızı çapraz çizgi; düşerken mühür gibi basılır (`ban-in`). Boş yuva çizgili. Sonraya: ban düşünce 2 sn'lik büyük animasyon (splash gri, çizgi, "BANLANDI"; kullanıcı isteği). |
| Havuz | `.pool-item`, `.role-filter` | Rol filtreleri oyunun pozisyon ikonlarıyla (CommunityDragon, altın renkli, temanın vurgu rengiyle aynı; `web/public/roles`, kullanıcı kararı). Üzerine gelince hafif büyüme + altın ışık; fearless kilidi 🔒 rozet; onay düğmesi ban'da kırmızı "Banla", pick'te "Kilitle". |
| İndirme | `web/scripts/fetch-champion-icons.mjs` | Splash'ler ve rol ikonları aynı komutla. Fiddlesticks'in centered splash'i sadece `FiddleSticks` adıyla var (`SPLASH_KEY` istisnası); ağ kopmasında (ECONNRESET) 3 kez yeniden dener. Splash'ler kaydedilmeden 640×360'a küçültülür (`sharp`, sadece geliştirme bağımlılığı; kullanıcı onaylı): tam boy 170 tane 18 MB, küçülmüş 3,8 MB (kart en fazla ~240×76, 2x ekranda 480 genişlik). Yama artık sabitlenebilir (`npm run icons -- 16.22.1`): Data Dragon 16.23.1'e geçmişti, en son yamayla çalıştırmak portreleri ve haritayı da değiştirirdi. Not: CommunityDragon'un 16.22 klasöründeki harita ikonları sonradan değişmiş; harita ikonları commit'teki halinde bırakıldı. |
| Erişilebilirlik, telefon | `index.css` | `prefers-reduced-motion`: animasyonlar kapanır. ≤ 760 px: takımlar üstte yan yana, havuz altta; ızgara sütunları `minmax(0, 1fr)` (içerik sütunu genişletmesin), ban ikonları 26 px. 375 px'te taşma yok (başsız Edge ile ölçüldü). |

Doğrulama: draft'ın iki anı (pick 1'de sıra mavide 14 sn; ban 1'de 3 sn) bileşenle çizilip başsız Edge'le ekran görüntüsü alındı (masaüstü 1200 px ve telefon 375 px); web 41 test + tsc + lint + build.
