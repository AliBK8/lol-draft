namespace LolDraft.Application.Matches;

/// <summary>Replay'lerin saklama süresi (DESIGN.md §8.16). 0: hiç silinmez.</summary>
public sealed record MatchRetention
{
    public static readonly MatchRetention Default = new(180);

    public MatchRetention(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        Days = days;
    }

    public int Days { get; }
}

/// <summary>
/// Saklama süresini aşan maçları siler (günde bir, Api'deki zamanlayıcı çağırır). Silinen maçın paylaşılmış linki
/// "maç bulunamadı" (404) verir; seri ve lobi bundan etkilenmez (lobiler zaten bellekte ve kısa ömürlü).
/// </summary>
public sealed class PruneMatches(IMatchRepository matches, IClock clock, MatchRetention retention)
{
    public Task<int> Handle(CancellationToken ct) =>
        retention.Days == 0
            ? Task.FromResult(0)
            : matches.DeleteOlderThan(clock.Now + TimeSpan.FromDays(-retention.Days), ct);
}
