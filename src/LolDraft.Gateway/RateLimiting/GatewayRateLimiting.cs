using System.Globalization;
using System.Threading.RateLimiting;

namespace LolDraft.Gateway.RateLimiting;

/// <summary>
/// HTTP rate limiting (DESIGN.md §8.5). Hepsi IP'ye göre bölümlenir; her IP'nin kendi sayacı var. SignalR mesajları
/// buradan geçmez (bir WebSocket bağlantısının içindeki mesajlar tek bir HTTP isteğidir): onlar API'de IHubFilter'la.
/// Sayaçlar bellekte (tek instance); birden fazla gateway'e geçilirse dağıtık depolama gerekir.
/// </summary>
internal static class GatewayRateLimiting
{
    // Rota politikalarının adları; appsettings.json'daki YARP rotaları bunlara RateLimiterPolicy ile bağlanır.
    public const string LobbyCreate = "lobby-create";
    public const string LobbyJoin = "lobby-join";
    public const string Replay = "replay";
    public const string HubConnect = "hub-connect";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            // Global: her IP'ye token bucket, 100 jeton, 10 sn'de bir dolar. Her istek önce buradan geçer; patlama
            // (sayfa açılışı, birkaç hızlı istek) serbest, sürekli yüksek hız değil.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetTokenBucketLimiter(ClientIp(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                }));

            // Lobi oluşturma: dakikada 5. Her lobi sunucuda durum tutar; sınırsız olsa bellek doldurulabilir.
            options.AddPolicy(LobbyCreate, context => FixedWindow(context, permits: 5));
            // Lobiye katılma: dakikada 10. Davet kodu 128 bit (tahmin edilemez); sınır yine de deneme yanılmayı kapatır.
            options.AddPolicy(LobbyJoin, context => FixedWindow(context, permits: 10));
            // Tekrar izleme: dakikada 60 (olay listesi büyük bir cevap).
            options.AddPolicy(Replay, context => FixedWindow(context, permits: 60));
            // Hub: IP başına aynı anda en fazla 5 bağlantı. WebSocket isteği bağlantı açık kaldıkça sürer; eşzamanlılık
            // sınırı izni bağlantı kapanınca geri verir, yani "açık bağlantı sayısı" sınırı olur.
            options.AddPolicy(HubConnect, context => RateLimitPartition.GetConcurrencyLimiter(ClientIp(context),
                _ => new ConcurrencyLimiterOptions { PermitLimit = 5, QueueLimit = 0 }));

            // Aşım: 429 + Retry-After (istemci ne kadar beklemesi gerektiğini bilir).
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    // İstemcinin anahtarı (IPv4 adresi ya da IPv6 /64 öneki, bkz. ClientKey). Önümüzde güvenilen bir proxy varsa
    // ForwardedHeaders onun X-Forwarded-For'undan adresi yazar; yoksa bağlantının kendi adresi. İstemcinin gönderdiği
    // X-Forwarded-For'a doğrudan bakılmaz (taklit edilebilir).
    private static string ClientIp(HttpContext context) => ClientKey.Of(context);
}
