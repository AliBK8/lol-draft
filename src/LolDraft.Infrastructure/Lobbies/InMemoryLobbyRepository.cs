using System.Collections.Concurrent;
using LolDraft.Application;
using LolDraft.Domain;

namespace LolDraft.Infrastructure.Lobbies;

/// <summary>
/// Aktif lobiler bellekte (karar 2026-11-10): canlı draft her hamlede değişir, diske yazmak gereksiz yük.
/// Bedeli: sunucu yeniden başlarsa açık lobiler düşer; tek API kopyası varsayılır (DESIGN.md §8.8).
/// </summary>
internal sealed class InMemoryLobbyRepository : ILobbyRepository
{
    private readonly ConcurrentDictionary<LobbyCode, Lobby> _lobbies = new();
    // Lobi → açanın anahtarı (IP). Domain bunu bilmez: kötüye kullanım sınırı depo/Application işi.
    private readonly ConcurrentDictionary<LobbyCode, string> _owners = new();
    // Ekleme tek kapıdan: "kaç lobisi var?" sayımı ile ekleme arasına başka ekleme giremesin.
    private readonly Lock _addLock = new();

    public Task<bool> TryAdd(Lobby lobby, string owner, int maxPerOwner, CancellationToken ct)
    {
        lock (_addLock)
        {
            if (_owners.Values.Count(o => o == owner) >= maxPerOwner)
                return Task.FromResult(false);

            // 128 bit rastgele kodun çakışması pratikte imkânsız; olursa rastgelelik kaynağı bozuktur.
            if (!_lobbies.TryAdd(lobby.Code, lobby))
                throw new InvalidOperationException("Lobi kodu çakıştı.");

            _owners[lobby.Code] = owner;
            return Task.FromResult(true);
        }
    }

    public void Remove(LobbyCode code)
    {
        _lobbies.TryRemove(code, out _);
        _owners.TryRemove(code, out _);
    }

    public IReadOnlyCollection<LobbyCode> Codes() => [.. _lobbies.Keys];

    public Task<Result<T>> Execute<T>(LobbyCode code, Func<Lobby, Result<T>> action, CancellationToken ct)
    {
        if (!_lobbies.TryGetValue(code, out var lobby))
            return Task.FromResult(Result<T>.Fail(LobbyErrors.NotFound));

        // Lobi başına kilit: Lobby kendi içinde eşzamanlılık bilmez (saf domain), sırayı depo sağlar.
        // Farklı lobiler birbirini bekletmez. Lobi nesnesi bu sınıfın dışına çıkmadığı için
        // üzerine başka kimse kilit alamaz.
        lock (lobby)
        {
            // Kilidi beklerken lobi silinmiş olabilir (saat kapattı): silinmiş lobide işlem yapılmaz.
            if (!_lobbies.ContainsKey(code))
                return Task.FromResult(Result<T>.Fail(LobbyErrors.NotFound));

            return Task.FromResult(action(lobby));
        }
    }
}
