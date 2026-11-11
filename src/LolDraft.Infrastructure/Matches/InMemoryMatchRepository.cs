using System.Collections.Concurrent;
using LolDraft.Application;
using LolDraft.Application.Matches;
using LolDraft.Domain;

namespace LolDraft.Infrastructure.Matches;

/// <summary>
/// Oynanmış maçlar bellekte (4f-1; 4f-2'de SQLite'a geçer, port aynı kalır). Sunucu yeniden başlarsa replay'ler düşer.
/// Sınırlı: en fazla <see cref="Capacity"/> maç, dolunca en eskisi atılır. Bir maç ~50 KB (≈ 700 küçük nesne:
/// olaylar + dakika kareleri); 2.000 maç ≈ 100 MB. Sınırsız olsa uzun süre açık kalan sunucunun belleği dolardı.
/// </summary>
internal sealed class InMemoryMatchRepository : IMatchRepository
{
    public const int Capacity = 2000;

    private readonly ConcurrentDictionary<MatchId, MatchRecord> _matches = new();
    private readonly ConcurrentQueue<MatchId> _order = new();
    private readonly Lock _evictLock = new();

    public Task Save(MatchRecord match, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(match);

        if (_matches.TryAdd(match.Id, match))
            _order.Enqueue(match.Id);

        // Fazlası eskiden yeniye atılır; iki kayıt aynı anda atmaya kalkmasın diye tek kapı.
        lock (_evictLock)
        {
            while (_matches.Count > Capacity && _order.TryDequeue(out var oldest))
                _matches.TryRemove(oldest, out _);
        }

        return Task.CompletedTask;
    }

    public Task<MatchRecord?> Get(MatchId id, CancellationToken ct) =>
        Task.FromResult(_matches.GetValueOrDefault(id));
}
