using LolDraft.Application.Matches;

namespace LolDraft.Api.Background;

/// <summary>
/// Replay saklama süresi (DESIGN.md §8.16): açılışta bir kez, sonra günde bir saklama süresini aşan maçları siler.
/// Karar <see cref="PruneMatches"/>'te; bu servis sadece zamanlar. Hata saati durdurmaz, ertesi gün yeniden dener.
/// </summary>
internal sealed class MatchRetentionService(PruneMatches pruneMatches, ILogger<MatchRetentionService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var deleted = await pruneMatches.Handle(stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Saklama süresi dolan {Count} maç silindi.", deleted);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Eski maçlar silinemedi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
