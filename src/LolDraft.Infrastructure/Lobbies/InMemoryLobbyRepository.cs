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

    public Task Add(Lobby lobby, CancellationToken ct)
    {
        // 128 bit rastgele kodun çakışması pratikte imkânsız; olursa rastgelelik kaynağı bozuktur.
        if (!_lobbies.TryAdd(lobby.Code, lobby))
            throw new InvalidOperationException("Lobi kodu çakıştı.");

        return Task.CompletedTask;
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
            return Task.FromResult(action(lobby));
        }
    }
}
