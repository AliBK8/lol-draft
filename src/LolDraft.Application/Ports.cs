using LolDraft.Application.Lobbies;
using LolDraft.Domain;

namespace LolDraft.Application;

// Portlar (DESIGN.md §8.3): Application dış dünyayı bu arayüzlerle görür; gerçekleri Infrastructure'da.

/// <summary>Aktif lobilerin deposu. Şu an bellekte; arkası değişse de kullanan kod değişmez.</summary>
public interface ILobbyRepository
{
    Task Add(Lobby lobby, CancellationToken ct);

    /// <summary>
    /// Lobiyi bulur ve işlemi o lobinin kilidi altında çalıştırır: aynı lobiye aynı anda gelen iki istek
    /// sırayla işlenir (ör. iki kişi aynı boş koltuğa oturamaz). Lobi yoksa <see cref="LobbyErrors.NotFound"/>.
    /// Canlı <see cref="Lobby"/> dışarı verilmez; işlem ihtiyacı olanı sonuç olarak döndürür.
    /// </summary>
    Task<Result<T>> Execute<T>(LobbyCode code, Func<Lobby, Result<T>> action, CancellationToken ct);
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

/// <summary>Oyunun şampiyon listesi. Şimdilik sadece kimlikler; profil verisi simülasyon adımında eklenecek.</summary>
public interface IChampionCatalog
{
    IReadOnlyCollection<ChampionId> All { get; }
}
