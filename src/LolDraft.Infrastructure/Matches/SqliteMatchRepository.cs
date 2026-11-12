using LolDraft.Application;
using LolDraft.Application.Matches;
using LolDraft.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LolDraft.Infrastructure.Matches;

/// <summary>
/// Oynanmış maçlar SQLite'ta (DESIGN.md §8.16). Depo singleton, her işlem kendi kısa ömürlü DbContext'iyle
/// (DbContext iş parçacığı güvenli değil; fabrika aynı anda gelen kayıtları ayırır). Sorgular EF ile parametreli.
/// </summary>
internal sealed class SqliteMatchRepository(
    IDbContextFactory<MatchDbContext> contexts, ILogger<SqliteMatchRepository> logger) : IMatchRepository
{
    public async Task Save(MatchRecord match, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(match);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            db.Matches.Add(MatchRow.Of(match));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Port sözleşmesi: fırlatmaz. Maç kimliği loglanmaz (tekrar izleme linkinin kendisi, DESIGN.md §8.6).
            logger.LogError(error, "Maç kaydedilemedi (maç {GameNumber}); tekrar izleme linki 404 verecek.", match.GameNumber);
        }
    }

    public async Task<MatchRecord?> Get(MatchId id, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await db.Matches.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id.Value, ct);
        return row?.ToRecord();
    }

    public async Task<int> DeleteOlderThan(Instant cutoff, CancellationToken ct)
    {
        var cutoffMs = cutoff.Value.ToUnixTimeMilliseconds();
        await using var db = await contexts.CreateDbContextAsync(ct);
        // Tek DELETE ... WHERE PlayedAt < @p (satırlar belleğe yüklenmez; PlayedAt indeksli).
        return await db.Matches.Where(m => m.PlayedAt < cutoffMs).ExecuteDeleteAsync(ct);
    }
}
