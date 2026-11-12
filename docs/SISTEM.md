# Sistem tasarımı (§8.1-8.9)

Tasarım dokümanının parçası; harita ve ilkeler: [DESIGN.md](DESIGN.md). Bölüm numaraları (§) bütün dosyalarda ortaktır.

---

## 8. Sistem tasarımı

### 8.1 Genel bakış

Dışarıya tek bir giriş noktası açılır: **Gateway**. Oyun API'si ve SignalR hub'ı sadece iç ağda çalışır, internetten doğrudan erişilemez. Simülasyon motoru ASP.NET'ten bağımsız saf bir kütüphanedir. Veri hattı offline çalışır; oyun sunucusu Riot API'sine hiç bağlanmaz ve Riot anahtarını tutmaz.

```
 İnternet
    │  HTTPS / WSS (443)
    ▼
┌───────────────────────────────┐
│ Gateway (YARP, ASP.NET Core)  │  TLS · güvenlik başlıkları · CORS · rate limiting
│                               │  JWT doğrulama · istek boyutu sınırı · correlation id
└──────────────┬────────────────┘
               │ iç ağ (docker network), sadece gateway erişebilir
               ▼
┌───────────────────────────────┐
│ Game.Api                      │  Minimal API'ler + DraftHub (SignalR)
│  └ Application (use case'ler) │  CreateLobby, JoinLobby, SubmitDraftAction, StartMatch, GetReplay
│     ├ Domain (Draft, Lobby)   │  draft durum makinesi, kurallar, fearless kilitleri
│     └ Engine (simülasyon)     │  saf, seed'li, deterministik
│  └ Infrastructure             │  EF Core + SQLite, şampiyon/tablo JSON yükleyici, saat
└──────────────┬────────────────┘
               ▼
         SQLite (volume)

 data-pipeline (Python, offline) ──► out/*.json ──► Infrastructure'a dosya olarak kopyalanır
```

Web istemcisi (React build'i) statik dosya olarak gateway üzerinden sunulur. Böylece tarayıcı tek bir origin görür ve CORS minimum kalır.

**Neden gateway?** Tek uygulama için zorunlu değil. Ama tek giriş noktası; rate limiting, TLS, güvenlik başlıkları ve kimlik doğrulamayı API kodundan ayırır. İleride ikinci bir servis (ör. ayrı bir simülasyon işçisi) eklendiğinde dışarıya hiçbir şey değişmez. Bedeli ek bir süreç ve bir ağ sıçraması.

### 8.2 Projeler ve katmanlar

```
src/
  LolDraft.Engine/          saf simülasyon (bağımlılık yok)
  LolDraft.Domain/          Lobby, DraftSession, kurallar (Engine'e bağımlı değil)
  LolDraft.Application/     use case'ler, port arayüzleri (Domain + Engine'e bağımlı)
  LolDraft.Infrastructure/  EF Core, JSON yükleyiciler, saat, CSPRNG (Application portlarını uygular)
  LolDraft.Api/             HTTP uç noktaları, DraftHub, DTO'lar, doğrulama (Application'a bağımlı)
  LolDraft.Gateway/         YARP reverse proxy, rate limiting, auth, güvenlik başlıkları
tests/
  LolDraft.Engine.Tests/    deterministiklik, denge
  LolDraft.Domain.Tests/    draft durum makinesi, fearless
  LolDraft.Api.Tests/       uç nokta + yetki testleri (WebApplicationFactory)
  LolDraft.Gateway.Tests/   gateway → API hattı uçtan uca (bellekte), rate limit, JWT, başlıklar
```

Diğer klasörler: `web/` (React + TypeScript), `data/` (oyun verisi: `champions.json`; API derlemeye gömer, denge aracı okur), `data-pipeline/` (Python), `prototype/mac-motoru.html` (referans JS motoru; `LolDraft.Engine` aynı kuralları uygular, aynı seed'le birebir aynı sonucu vermesi gerekmez).

**Bağımlılık kuralı:** Oklar içe doğru. `Api → Application → Domain / Engine`. `Infrastructure`, `Application`'daki arayüzleri uygular. `Domain` ve `Engine` ASP.NET, EF Core ya da dosya sistemi bilmez.

### 8.3 Kapsülleme kuralları

- **Varsayılan görünürlük `internal`.** Bir proje dışarıya sadece kendi giriş arayüzünü açar; testler `InternalsVisibleTo` ile erişir.
- **Sınıflar `sealed`**, kalıtım için açıkça tasarlanmadıysa.
- **Public setter yok.** Durum sadece anlamlı metotlarla değişir ve her metot kendi kuralını kontrol eder.
- **Değer nesneleri** ilkel tipler yerine: `ChampionId`, `LobbyCode`, `PlayerId`, `Seed`, `TeamSide`. Kurucu doğrular; geçersiz değer hiç oluşamaz.
- **Olaylar ve sonuçlar değişmez:** `record` + `IReadOnlyList`.
- **Hata = sonuç nesnesi**, beklenen iş hataları için exception değil: `Result<T>` (`NotYourTurn`, `ChampionUnavailable`, `PhaseClosed`…).

**Engine'in dışa açtığı yüzey (tamamı bu kadar):**
```csharp
public interface IMatchSimulator
{
    MatchResult Simulate(MatchInput input);           // aynı input → aynı sonuç
}
public static class MatchEngine { public static IMatchSimulator CreateSimulator(); }   // simülatör internal kalsın diye
public sealed record MatchInput(TeamDraft Blue, TeamDraft Red, Seed Seed);   // Directive'ler v0.2'de eklenecek
public sealed record MatchResult(IReadOnlyList<MatchEvent> Events, ScoreBreakdown Breakdown, MatchEnd End,
    IReadOnlyList<GoldFrame> GoldFrames);                // dakika kareleri (§16.11)
```
Yanında değer nesneleri ve olay tipleri: `Seed`, `TeamSide`, `ChampionProfile`, `TeamDraft`, `ScoreBreakdown`, `ScoreCategory`, `MatchEnd`, `GoldFrame`, `ChampionGold`, `MatchEvent` ve alt tipleri. Liste `ArchitectureTests` ile kilitli; yeni public tip bilinçli eklenir. Olaylar JSON bilmez, JSON'a çevirmek Api'nin işi (DTO).

`SynergyCalculator`, `LaneFlow`, `Narrator`, `FightResolver`, `MovementPlanner`, `IRandomSource` gibi parçalar `internal sealed`.

**Domain örneği: draft durum makinesi:**
```csharp
public sealed class DraftSession
{
    private readonly List<DraftAction> _actions = new();
    public DraftPhase Phase { get; private set; }
    public TeamSide Turn { get; private set; }
    public IReadOnlyList<DraftAction> Actions => _actions;

    public Result Ban(Side side, ChampionId champion, Instant now) { /* faz, süre, sıra, havuz, tekrar, fearless */ }
    public Result Pick(Side side, ChampionId champion, Instant now) { /* aynı kontroller */ }
    public Result SwapRoles(Side side, Role a, Role b, Instant now) { /* swap fazında iki yuvayı değiştir */ }
    public Result LockLineup(Side side, Instant now) { /* iki takım da kilitleyince draft biter */ }
    public Result Timeout(Instant now, IDraftRandom random) { /* sunucu sayacı: boş ban / rastgele uygun pick / swap biter */ }
}
```
Dışarıdan `Phase` ya da `Turn` set edilemez; geçersiz hamle `Result.Fail` döner ve durum değişmez.

**Aggregate kökü `Lobby` (2026-11-11):** `DraftSession` (Start, Ban, Pick, SwapRoles, LockLineup, Timeout) ve `Series` (Start, StartDraft, Forfeit, RecordGame) değiştiren metotları `internal`; Domain dışından sadece okunur. Hamle, zaman aşımı, hükmen ve maç sonucu `Lobby` üzerinden geçer: koltuk → renk çevirisi, `actionId` tekrar koruması ve `LastActivity` atlanamaz. 4f'de `RecordGame` için `Lobby.RecordGame` eklenir.

**Uygulama (4d, `LolDraft.Domain`):**
- Durum = sabit adım tablosu (`DraftOrder`, 20 adım) + adım sayacı; faz ve sıra tablodan okunur, ayrı değişken yok.
- Domain Engine'i bilmez (§8.2): draft bitince `DraftResult` (taraf → 5 yuvalık `TeamLineup` + hamleler) üretir; Application bunu `IChampionCatalog` ile motorun `TeamDraft`'ına çevirir. Domain'in kendi `Side`'ı var.
- Sadece açık draft (gizli/eşzamanlı seçim yok; karar 2026-11-04).
- Swap: 20. adımdan sonra her takımın pick'leri pick sırasıyla yuvalara dizilir (1. pick top … 5. pick support). Takım istediği kadar iki yuvayı yer değiştirir (arayüzde top pick'ine, sonra adc yuvasına tıklamak), sonra kilitler. İkisi de kilitleyince ya da süre dolunca o anki dizilimle biter.
- Zaman aşımı: ban → boş ban; pick → uygun (havuz − ban − seçilen − fearless kilidi) şampiyonlar id sırasıyla dizilir, zar birini seçer.
- Fearless kilidi (`FearlessLocks`, tek ortak liste) hem pick'i hem ban'ı engeller (kimsenin seçemeyeceği şampiyonu ban'lamak anlamsız).
- Havuz kuralı: kilitler düşüldükten sonra ≥ 20 şampiyon (10 ban + 10 pick) kalmalı, yoksa `pool_too_small`.

**Seri (`Series`, 4d):**
- Kimlik `TeamSlot` (A/B), seri boyunca sabit; renk (`Side`) maçlık. İlk mavi dışarıdan gelir (sunucu CSPRNG), sonra her maç değişir: `BlueTeam`, `SideOf(team)`, `TeamOn(side)`.
- Akış: `StartDraft(timings, now)` → draft oynanır → simülasyon → `RecordGame(kazanan renk)`. Seri kazanan rengi o maçta o renkteki takıma çevirir, skoru takıma yazar. Bitiş: bo1 → 1, bo3 → 2, bo5 → 3 galibiyet.
- Seri o anki draft'ın sahibidir (`CurrentDraft`): maç sürerken ikinci draft başlamaz (`game_in_progress`), draft bitmeden sonuç yazılmaz (`draft_not_completed`), biten seride ikisi de `series_finished`. Aynı maç iki kez yazılamaz.
- Fearless kilitleri: biten maçların bütün pick'leri (maç başına 10); ban'lar taşınmaz.
- Havuz kontrolü seri başlarken, en uzun hâle göre: `havuz − (maç sayısı − 1) × 10 ≥ 20`. 40'lık havuz: fearless bo3 tam sığar (3. maçta 20 kilit, 20 kalır), fearless bo5 sığmaz (4. maçta 30 kilit, 10 kalır) → `pool_too_small`. Kural havuz boyutundan hesaplandığı için havuz 60'a çıkınca bo5 kendiliğinden açılır.

**Application portları:**
```csharp
// Yazıldı (4e-1, Application/Ports.cs):
public interface ILobbyRepository      // lobi başına kilit: işlem kilidin altında koşar, canlı Lobby dışarı çıkmaz
{
    Task Add(Lobby lobby, CancellationToken ct);
    Task<Result<T>> Execute<T>(LobbyCode code, Func<Lobby, Result<T>> action, CancellationToken ct);
}
public interface IGuestTokenService { string Issue(GuestIdentity identity); Task<GuestIdentity?> Read(string token); }
public interface ISecureRandom { byte[] Bytes(int count); }   // CSPRNG: lobi kodu, oyuncu kimliği, yazı tura
public interface IClock { Instant Now { get; } }
public interface IChampionCatalog { IReadOnlyCollection<ChampionId> All { get; } }   // profil verisi simülasyon adımında

// Sırada (simülasyon adımı):
public interface IMatchRepository { Task Save(MatchRecord record, CancellationToken ct); Task<MatchRecord?> Get(MatchId id, CancellationToken ct); }
public interface ISeedGenerator { Seed Next(); }   // CSPRNG
```

### 8.4 Kimlik ve yetki (kayıtsız)

Kayıt yok ama her istek bir kimliğe bağlı.

1. **Lobi oluşturma:** `POST /api/lobbies` → sunucu `LobbyCode` (128 bit rastgele, URL-güvenli) ve oluşturana bir **misafir JWT'si** üretir.
2. **Davet linki:** `/l/{LobbyCode}`. Kod tahmin edilemez; boş koltuk bir kez alınır.
3. **Katılma:** `POST /api/lobbies/{code}/join` → ikinci oyuncuya JWT.
4. **JWT içeriği:** `sub` (PlayerId), `lobby` (LobbyCode), `seat` (takım koltuğu A/B; renk değil, renk her maç seriden gelir), `exp` (2 saat). HMAC-SHA256 ile imzalanır, anahtar ortam değişkeninde.
5. **Yeniden bağlanma (ertelendi, karar 2026-11-10):** JWT 2 saat geçerli; istemci saklarsa sayfa yenileme çalışır. Token kaybolursa koltuğu geri almak için ileride: JWT ile birlikte uzun ömürlü olmayan bir `rejoinToken` (tek kullanımlık, lobi ömrü kadar) verilir. Sayfa yenilenirse koltuk geri alınır.
6. **Doğrulama:** Gateway JWT imzasını ve süresini kontrol eder; geçersizse isteği API'ye hiç iletmez. API ayrıca **yetkiyi** kontrol eder: oyuncu bu lobide mi, bu koltukta mı, sıra onda mı. Yetki kontrolü domain'in işidir, gateway'in değil.
7. SignalR bağlantısında token `access_token` sorgu parametresiyle gelir (WebSocket başlık gönderemez); gateway ve hub bu parametreyi okur. Bu parametre loglara yazılmaz.

### 8.5 Rate limiting

İki katmanda: HTTP istekleri gateway'de, SignalR mesajları API'de (bir WebSocket bağlantısı içindeki mesajlar gateway'in HTTP limitlerinden geçmez).

**Gateway politikaları** (ASP.NET Core `RateLimiter` + YARP route politikası):

| Politika | Uygulandığı yer | Bölümleme | Limit |
|---|---|---|---|
| `global` | tüm rotalar | IP | token bucket, 10 sn'de 100 istek |
| `lobby-create` | `POST /api/lobbies` | IP | sabit pencere, dakikada 5 |
| `lobby-join` | `POST /api/lobbies/{code}/join` | IP | dakikada 10 |
| `replay` | `GET /api/matches/{id}` | IP | dakikada 60 |
| `hub-connect` | `/hubs/draft` (negotiate + WS) | IP | eşzamanlı en fazla 5 bağlantı |
| `simulate-heavy` | Monte Carlo / "ne olurdu?" uçları | oyuncu (JWT `sub`) | eşzamanlılık: sunucu genelinde 4 iş, kuyruk 20; oyuncu başına dakikada 3 |

**Hub içi limit** (`IHubFilter`, API'de): bağlantı başına saniyede 5 mesaj, dakikada 60. Aşılırsa mesaj reddedilir; tekrar ederse bağlantı kapatılır.

- Aşımda `429 Too Many Requests` + `Retry-After` döner. Hub'da istemciye `RateLimited` olayı gider.
- IP, gateway önünde başka bir proxy varsa `ForwardedHeaders` ile ve **sadece güvenilen proxy listesinden** okunur; aksi halde `X-Forwarded-For` taklit edilip limit atlatılabilir.
- **"IP" = istemci anahtarı** (`Gateway/ClientKey.cs`, 2026-11-11): IPv4 adresin kendisi, IPv6'da **/64 öneki** (`2001:db8:1:2::/64`). Operatör IPv6'da müşteriye bütün bir /64 verir; tam adrese göre sayılsa tek kişi her istekte adres değiştirip bütün sınırları atlardı. IPv4'ün IPv6 kılığı (`::ffff:a.b.c.d`) IPv4 sayılır. Aynı anahtar API'ye `X-Client-Ip` başlığıyla gider: "IP başına 3 lobi" de aynı kovayı sayar.
- Bilinçli ödünleşim: aynı NAT arkasındaki bir grup (internet kafe, yurt, LAN turnuvası) tek istemci sayılır (en fazla 3 açık lobi, 5 hub bağlantısı). Sorun olursa sınırlar yükseltilir; IP'den başka güvenilir ayırıcı yok (kayıt yok).
- Limitler bellekte tutulur (tek instance). Birden fazla gateway instance'ına geçilirse dağıtık limit (Redis) gerekir.

### 8.6 Güvenlik önlemleri

**Oyun bütünlüğü**
- Sunucu otoriter: istemci sadece niyet gönderir (`BanChampion`, `PickChampion`). Sonuç, zar ve puan istemciden asla alınmaz.
- Her draft hamlesi domain'de doğrulanır: sıra, faz, sayaç, şampiyonun havuzda olması, ban/pick tekrarı, fearless kilidi.
- Seed sunucuda `RandomNumberGenerator` (CSPRNG) ile üretilir ve maç bitene kadar istemciye gönderilmez; tekrar izleme maç sonrası açılır.
- Gizli bilgi (ileride eşzamanlı gizli seçimler) çözülene kadar yayınlanmaz; draft durum DTO'su sadece görülmesi gerekeni taşır.
- Sayaçlar sunucuda (`BackgroundService`); istemcinin gönderdiği zaman kullanılmaz.
- Hamleler idempotent: istemci her hamleye bir `actionId` ekler, aynı id ikinci kez işlenmez (yeniden bağlanma ve çift tıklama).

**Girdi ve çıktı**
- DTO doğrulaması (FluentValidation): `ChampionId` beyaz listeden, `LobbyCode` biçimi, takma ad en fazla 20 karakter ve izinli karakter seti.
- React metni otomatik kaçışlar; `dangerouslySetInnerHTML` kullanılmaz. Olay metinleri sunucuda şablondan üretilir, kullanıcı girdisi içermez (takma ad hariç, o da kaçışlanır).
- EF Core parametreli sorgular; ham SQL yok.

**Taşıma ve başlıklar (gateway)**
- Sadece HTTPS/WSS; HTTP → HTTPS yönlendirme, HSTS.
- `AllowedHosts` (Host başlığı beyaz listesi): geliştirmede `*`, production'da gerçek alan adı (ortam değişkeni `AllowedHosts=draft.example.com`). Production'da `*` ya da boş kalırsa gateway açılmaz (2026-11-11); başka alan adına gelen istek 400.
- `Content-Security-Policy` (sadece kendi origin'i ve gerekiyorsa Google Fonts), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'none'`.
- CORS sadece web origin'ine açık (web aynı origin'den sunulursa CORS kapalı kalabilir).

**DoS ve kaynak sınırları**
- İstek gövdesi sınırı: 16 KB (gateway ve Kestrel).
- SignalR: `MaximumReceiveMessageSize` 4 KB, `KeepAliveInterval` 15 sn, `ClientTimeoutInterval` 30 sn.
- Lobi yaşam süresi: 30 dk hareketsiz lobi silinir. IP başına (§8.5 istemci anahtarı) en fazla 3 açık lobi.
- Toplam açık lobi tavanı 2.000 (`LobbyCapacity`, 2026-11-11): kişi başı sınır tek kaynaktan gelen seli durdurur, tavan çok kaynaktan geleni. Dolunca yeni lobi `503 server_busy`; açık oyunlar etkilenmez. Sayma ve ekleme aynı kilit altında.
- Ağır simülasyon işleri sınırlı eşzamanlılıkla kuyrukta çalışır (§8.5 `simulate-heavy`).

**Ağ ve dağıtım**
- API ve veritabanı sadece iç docker ağında; dışarıya sadece gateway'in 443 portu açık.
- API, gateway'den gelmeyen istekleri reddeder (iç ağ + gateway'in eklediği paylaşılan gizli başlık; ileride mTLS).
- Konteynerler root olmayan kullanıcıyla çalışır.

**Sırlar ve kayıt**
- JWT imza anahtarı, iç başlık sırrı vb. ortam değişkeninde (geliştirmede `dotnet user-secrets`); repoya girmez.
- Oyun sunucusu Riot anahtarını tutmaz (veri hattı offline).
- Yapılandırılmış log (Serilog), her istekte correlation id; token, rejoinToken ve IP dışı kişisel veri loglanmaz. Draft hamleleri denetim kaydı olarak tutulur.

**Bağımlılıklar**
- `dotnet list package --vulnerable` ve `npm audit` CI'da; Dependabot açık.

### 8.7 API yüzeyi

| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/api/lobbies` | yok | Lobi oluştur, JWT + davet kodu döner |
| POST | `/api/lobbies/{code}/join` | yok | Boş koltuğa katıl, JWT döner |
| POST | `/api/lobbies/{code}/rejoin` | rejoinToken | Koltuğu geri al (ertelendi; gateway rotası hazır, API'de uç nokta yok) |
| GET | `/api/lobbies/{code}` | JWT (lobide) | `{yourSeat, lobby}`: koltuk + hub'daki `LobbyUpdated` ile aynı biçimde durum |
| GET | `/api/matches/{id}` | yok (paylaşım linki; kimlik 128 bit) | Oynanmış maç: olaylar, dakika kareleri, karne (§8.15); diskteki dondurulmuş belge, `MatchReady` ile aynı (§8.16) |
| WS | `/hubs/draft` | JWT | `Ready`, `Ban`, `Pick`, `SwapRoles`, `LockLineup` (her biri `actionId` ile) → `LobbyUpdated` (tam durum + `deadline` + `serverNow` + `revision` + `games`; saniyelik `TimerTick` yok, karar 2026-11-10), `MatchReady` (draft bitince oynanan maç, §8.15), `LobbyClosed` |

### 8.8 Dağıtım

`docker-compose`:
- `gateway`: 443 dışarı açık, React build'i ve TLS sertifikası (Let's Encrypt) burada.
- `api`: sadece iç ağ.
- `sqlite`: API konteynerine bağlı volume (yedekleme cron'u ile). API `MATCH_DB_PATH=/data/matches.db`, `MATCH_RETENTION_DAYS` (varsayılan 180); açılışta migration'ları kendisi uygular (§8.16).

Ölçeklendirme gerekirse sırasıyla: SQLite → PostgreSQL, SignalR için Redis backplane, rate limit için dağıtık depolama, simülasyon için ayrı işçi servisi.

### 8.9 Kodda nereye eklenecek?

| Konu | Proje / dosya |
|---|---|
| YARP rotaları, rate limit politikaları, JWT doğrulama, güvenlik başlıkları, CORS, ForwardedHeaders | `LolDraft.Gateway/Program.cs`, `appsettings.json` (`ReverseProxy` bölümü) |
| Hub mesaj limiti | `LolDraft.Api/Hubs/RateLimitHubFilter.cs` |
| Yetki kontrolü (lobi/koltuk/sıra) | `LolDraft.Domain/Draft/DraftSession.cs` ve `LolDraft.Application/Draft/SubmitDraftActionHandler.cs` |
| DTO doğrulama | `LolDraft.Api/Validation/*Validator.cs` |
| JWT üretimi, rejoin token | `LolDraft.Infrastructure/Auth/GuestTokenService.cs` (arayüz `Application`'da) |
| CSPRNG seed | `LolDraft.Infrastructure/Random/CryptoSeedGenerator.cs` |
| Sunucu sayacı | `LolDraft.Api/Background/DraftTimerService.cs` |
| Lobi temizliği | `LolDraft.Api/Background/LobbyJanitorService.cs` |
| İç başlık kontrolü (sadece gateway'den gelen) | `LolDraft.Api/Middleware/GatewayOnlyMiddleware.cs` |
| Simülasyon motoru | `LolDraft.Engine/` (sadece `IMatchSimulator` public) |
