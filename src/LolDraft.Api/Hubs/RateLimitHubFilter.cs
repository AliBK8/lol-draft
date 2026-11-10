using LolDraft.Api.Lobbies;
using LolDraft.Domain;
using Microsoft.AspNetCore.SignalR;

namespace LolDraft.Api.Hubs;

/// <summary>
/// Hub mesaj limiti (DESIGN.md §8.5): bir WebSocket bağlantısının içindeki mesajlar gateway'in HTTP limitlerinden
/// geçmez, o yüzden limit burada, bağlantı başına. Saniyede 5, dakikada 60 mesaj; aşan mesaj hiç işlenmez,
/// yapana <c>rate_limited</c> döner. Bir dakikada 10 kez limite takılan bağlantı kapatılır (bot ya da bozuk istemci;
/// normal oyuncu bu hızlara yaklaşmaz: bir draft'ta ~30 hamle, adım başına 30 sn).
/// </summary>
internal sealed class RateLimitHubFilter(TimeProvider time, ILogger<RateLimitHubFilter> logger) : IHubFilter
{
    public static readonly Error RateLimited = new("rate_limited", "Çok hızlı mesaj gönderiyorsun.");

    private const int PerSecond = 5;
    private const int PerMinute = 60;
    private const int StrikesBeforeClose = 10;

    private static readonly object LimitsKey = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var context = invocation.Context;
        var limits = (Limits)context.Items[LimitsKey]!;

        // Önce saniye, sonra dakika: saniyeye takılan mesaj dakika bütçesini harcamaz.
        if (limits.Second.TryAdd() && limits.Minute.TryAdd())
            return await next(invocation);

        // Her ret bir "ihtar"; dakikada 10 ihtar → bağlantı kapanır.
        if (!limits.Strikes.TryAdd())
        {
            logger.LogWarning("Mesaj limiti tekrar tekrar aşıldı, bağlantı kapatılıyor: {ConnectionId}", context.ConnectionId);
            context.Abort();
        }

        return HubResultDto.Failure(RateLimited);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        // Sayaçlar bağlantıyla yaşar, bağlantı kapanınca onunla birlikte gider (ayrı temizlik gerekmez).
        context.Context.Items[LimitsKey] = new Limits(time);
        return next(context);
    }

    // 10. ihtarda kapanır: ilk 9 ihtar pencereye sığar, 10.'su sığmaz.
    private sealed class Limits(TimeProvider time)
    {
        public SlidingWindow Second { get; } = new(PerSecond, TimeSpan.FromSeconds(1), time);
        public SlidingWindow Minute { get; } = new(PerMinute, TimeSpan.FromMinutes(1), time);
        public SlidingWindow Strikes { get; } = new(StrikesBeforeClose - 1, TimeSpan.FromMinutes(1), time);
    }
}
