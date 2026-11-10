using System.Security.Cryptography;
using System.Text;

namespace LolDraft.Api.Middleware;

/// <summary>
/// Sadece gateway'den gelen isteği kabul eder (DESIGN.md §8.6 "Ağ ve dağıtım"). API zaten iç ağda; bu ikinci kilit:
/// ağ yanlışlıkla açılırsa bile gateway'i atlatan istek (rate limit, JWT, başlıklar olmadan) içeri giremez.
/// Gateway her isteğe paylaşılan sırrı <see cref="HeaderName"/> başlığıyla ekler; istemcinin kendi gönderdiğini siler.
/// </summary>
internal sealed class GatewayOnlyMiddleware(RequestDelegate next, ApiSettings settings, ILogger<GatewayOnlyMiddleware> logger)
{
    public const string HeaderName = "X-Gateway-Secret";

    /// <summary>
    /// İstemcinin IP'si, gateway'in gördüğü hâliyle. Gateway istemcinin gönderdiğini siler, kendi değerini yazar;
    /// API'ye sadece gateway ulaştığı için (bu middleware) bu başlığa güvenilir.
    /// </summary>
    public const string ClientIpHeader = "X-Client-Ip";

    // Sırrın SHA-256 özeti bir kez hesaplanır; gelen değerin özetiyle karşılaştırılır.
    private readonly byte[] _expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.GatewaySecret));

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsFromGateway(context.Request.Headers[HeaderName]))
        {
            // 404: API'nin varlığını bile belli etme (403 "burada bir şey var ama giremezsin" der).
            logger.LogWarning("Gateway dışından istek reddedildi: {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }

    // Tam olarak bir değer olmalı ve sırla eşleşmeli. Karşılaştırma sabit zamanlı: düz == ilk farklı baytta durur,
    // saldırgan cevap süresinden sırrın kaç baytını bildiğini ölçebilir (timing attack). Önce iki tarafı da SHA-256'dan
    // geçirmek uzunluğu eşitler: FixedTimeEquals farklı uzunlukta hemen döner, uzunluğu sızdırırdı.
    private bool IsFromGateway(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
            return false;

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        return CryptographicOperations.FixedTimeEquals(actualHash, _expectedHash);
    }
}
