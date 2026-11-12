using LolDraft.Application.Lobbies;
using LolDraft.Application.Matches;
using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application;

// Portlar (DESIGN.md §8.3): Application dış dünyayı bu arayüzlerle görür; gerçekleri Infrastructure'da.

/// <summary>Aktif lobilerin deposu. Şu an bellekte; arkası değişse de kullanan kod değişmez.</summary>
public interface ILobbyRepository
{
    /// <summary>
    /// Lobiyi ekler. Eklemez: toplam açık lobi <paramref name="maxTotal"/>'a ulaştıysa (sunucu dolu) ya da
    /// <paramref name="owner"/>'ın (istemci anahtarı: IPv4 ya da IPv6 /64) zaten <paramref name="maxPerOwner"/> açık
    /// lobisi varsa. Sayma ve ekleme tek adım: aynı anda gelen istekler sınırı birlikte aşamaz.
    /// </summary>
    Task<LobbyAddResult> TryAdd(Lobby lobby, string owner, int maxPerOwner, int maxTotal, CancellationToken ct);

    /// <summary>Lobiyi siler (sahibinin sayacı da düşer). <see cref="Execute{T}"/> içinden çağrılabilir.</summary>
    void Remove(LobbyCode code);

    /// <summary>Şu an açık lobilerin kodları (anlık kopya; lobi saati bunları sırayla ilerletir).</summary>
    IReadOnlyCollection<LobbyCode> Codes();

    /// <summary>
    /// Lobiyi bulur ve işlemi o lobinin kilidi altında çalıştırır: aynı lobiye aynı anda gelen iki istek
    /// sırayla işlenir (ör. iki kişi aynı boş koltuğa oturamaz). Lobi yoksa <see cref="LobbyErrors.NotFound"/>.
    /// Canlı <see cref="Lobby"/> dışarı verilmez; işlem ihtiyacı olanı sonuç olarak döndürür.
    /// </summary>
    Task<Result<T>> Execute<T>(LobbyCode code, Func<Lobby, Result<T>> action, CancellationToken ct);
}

/// <summary><see cref="ILobbyRepository.TryAdd"/> sonucu: eklendi mi, eklenmediyse hangi sınır yüzünden.</summary>
public enum LobbyAddResult
{
    Added,

    /// <summary>Bu istemcinin açık lobi sınırı dolu.</summary>
    OwnerLimitReached,

    /// <summary>Sunucunun toplam lobi sınırı dolu.</summary>
    ServerFull,
}

/// <summary>Misafir JWT'si (DESIGN.md §8.4): üretir ve doğrular.</summary>
public interface IGuestTokenService
{
    string Issue(GuestIdentity identity);

    /// <summary>İmza, süre, yayıncı ve hedef geçerliyse kimliği döndürür; değilse null.</summary>
    Task<GuestIdentity?> Read(string token);
}

/// <summary>Kriptografik rastgelelik (CSPRNG): lobi kodu, oyuncu kimliği, yazı tura.</summary>
public interface ISecureRandom
{
    byte[] Bytes(int count);
}

public interface IClock
{
    Instant Now { get; }
}

/// <summary>Oyunun şampiyon listesi: draft havuzu (kimlikler) ve motorun profilleri (data/champions.json).</summary>
public interface IChampionCatalog
{
    IReadOnlyCollection<ChampionId> All { get; }

    /// <summary>Şampiyonun motor profili (doğal rolleri dahil). Havuzda olmayan kimlik programcı hatasıdır.</summary>
    ChampionProfile Profile(ChampionId champion);
}

/// <summary>
/// Oynanmış maçlar (tekrar izleme, DESIGN.md §8.7, §8.16): SQLite'ta kalıcı.
/// Kayıt dondurulmuş belgenin kendisidir: replay seed'le yeniden simüle etmez (denge ayarı eski maçı değiştirirdi).
/// </summary>
public interface IMatchRepository
{
    /// <summary>
    /// Maçı yazar. Hata fırlatmaz (loglar): maç oynandı ve seriye yazıldı, yayın ve diğer lobilerin tiki bir disk
    /// hatası yüzünden durmamalı. Yazılamayan maçın sadece tekrar izleme linki 404 verir.
    /// </summary>
    Task Save(MatchRecord match, CancellationToken ct);

    Task<MatchRecord?> Get(MatchId id, CancellationToken ct);

    /// <summary>Oynanma anı <paramref name="cutoff"/>'tan eski maçları siler (saklama süresi); silinen sayısını döner.</summary>
    Task<int> DeleteOlderThan(Instant cutoff, CancellationToken ct);
}
