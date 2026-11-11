using System.Net;
using System.Text;

namespace LolDraft.Gateway;

/// <summary>
/// Gateway'in sırları ve ağ ayarları (DESIGN.md §8.4, §8.6). Sırlar ortam değişkeninden (geliştirmede dotnet
/// user-secrets) okunur, repoya girmez. Eksik ya da zayıfsa uygulama açılmaz (fail fast).
/// </summary>
internal sealed record GatewaySettings
{
    public const string GatewaySecretKey = "GATEWAY_SECRET";
    public const string JwtSigningKeyKey = "JWT_SIGNING_KEY";
    public const string TrustedProxiesKey = "Gateway:TrustedProxies";
    public const string AllowedHostsKey = "AllowedHosts";

    // Misafir JWT'sinin yayıncısı ve hedefi (§8.4): token başka bir uygulama için üretilmişse kabul edilmez.
    public const string JwtIssuer = "lol-draft";
    public const string JwtAudience = "lol-draft-game";

    // Paylaşılan sır en az 32 karakter; HMAC-SHA256 anahtarı en az 32 bayt (256 bit), daha kısası kaba kuvvete açık.
    private const int MinSecretLength = 32;
    private const int MinSigningKeyBytes = 32;

    private GatewaySettings(string gatewaySecret, byte[] jwtSigningKey, IReadOnlyList<IPAddress> trustedProxies)
    {
        GatewaySecret = gatewaySecret;
        JwtSigningKey = jwtSigningKey;
        TrustedProxies = trustedProxies;
    }

    /// <summary>API'ye "bu istek gateway'den" diyen sır (API'deki GatewayOnlyMiddleware ile aynı değer).</summary>
    public string GatewaySecret { get; }

    /// <summary>Misafir JWT'lerinin HMAC-SHA256 imza anahtarı (token'ı üreten API ile aynı değer).</summary>
    public byte[] JwtSigningKey { get; }

    /// <summary>X-Forwarded-For'una güvenilen önceki proxy'ler (ör. bulut yük dengeleyicisi). Boşsa hiçbirine.</summary>
    public IReadOnlyList<IPAddress> TrustedProxies { get; }

    public static GatewaySettings From(IConfiguration configuration, IHostEnvironment environment)
    {
        // Host başlığı beyaz listesi: geliştirmede "*" (localhost, test sunucusu), production'da gerçek alan adı
        // (ör. AllowedHosts=draft.example.com, birden fazlaysa ';' ile). "*" kalırsa uygulama başka alan adlarına
        // gelen istekleri de kabul eder: alan adı gizlenmiş bir proxy'nin arkasından ya da Host başlığı zehirlemeyle
        // üretilen linklerde bizim sunucu cevap verir. Unutulmasın diye açılış durur.
        var allowedHosts = configuration[AllowedHostsKey];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*"))
            throw Missing(AllowedHostsKey, "\"*\" (production'da gerçek alan adı olmalı)");

        var secret = configuration[GatewaySecretKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
            throw Missing(GatewaySecretKey, $"{MinSecretLength} karakterden kısa");

        var signingKey = Encoding.UTF8.GetBytes(configuration[JwtSigningKeyKey] ?? "");
        if (signingKey.Length < MinSigningKeyBytes)
            throw Missing(JwtSigningKeyKey, $"{MinSigningKeyBytes} bayttan kısa");

        var proxies = configuration.GetSection(TrustedProxiesKey).Get<string[]>() ?? [];
        return new GatewaySettings(secret, signingKey, proxies.Select(IPAddress.Parse).ToArray());
    }

    private static InvalidOperationException Missing(string key, string reason) =>
        new($"{key} eksik ya da {reason}. Ortam değişkeni ya da dotnet user-secrets ile ver.");
}
