using LolDraft.Api.Hubs;
using LolDraft.Api.Lobbies;
using LolDraft.Application.Lobbies;
using Microsoft.AspNetCore.SignalR;

namespace LolDraft.Api.Background;

/// <summary>
/// Sunucu sayacı (DESIGN.md §8.6, §8.13, §8.14): saniyede 4 kez lobileri ilerletir (zaman aşımı, hükmen galibiyet,
/// kapanış), durumu değişenleri ve kapananları yayınlar.
/// Karar Domain'de (<see cref="Domain.Lobby.Advance"/>); bu servis sadece zamanlar ve yayınlar.
/// Bir adımın süresi dolduktan en geç ~250 ms sonra zaman aşımı uygulanır.
/// </summary>
internal sealed class LobbyClockService(
    AdvanceLobbies advanceLobbies,
    IHubContext<DraftHub> hub,
    ILogger<LobbyClockService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var report = await advanceLobbies.Handle(stoppingToken);

                foreach (var view in report.Changed)
                {
                    await hub.Clients.Group(DraftHub.Group(view.Code))
                        .SendAsync(DraftHub.LobbyUpdated, LobbyStateDto.Of(view), stoppingToken);
                }

                // Kapanan lobi: bağlı kalan varsa haber alır (arayüz "lobi kapandı" der, bağlantıyı kendisi kapatır).
                foreach (var code in report.Closed)
                {
                    await hub.Clients.Group(DraftHub.Group(code))
                        .SendAsync(DraftHub.LobbyClosed, stoppingToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Tek bir tikteki hata saati durdurmasın; durursa hiçbir draft'ın süresi dolmaz.
                logger.LogError(error, "Lobi saati tikinde hata.");
            }
        }
    }
}
