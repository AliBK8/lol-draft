using System.Text;

namespace LolDraft.Api;

/// <summary>
/// API'nin sırları (DESIGN.md §8.6): ortam değişkeninden (geliştirmede dotnet user-secrets) okunur, repoya girmez.
/// Eksik ya da zayıfsa uygulama açılmaz (fail fast): sırsız çalışan bir API, gateway'i atlatan isteği ayırt edemez.
/// </summary>
internal sealed record ApiSettings
{
    // Gateway'le paylaşılan sır, en az 32 karakter (rastgele üretilmiş; ör. openssl rand -base64 48).
    public const string GatewaySecretKey = "GATEWAY_SECRET";
    // Misafir JWT'sinin imza anahtarı; gateway'deki JWT_SIGNING_KEY ile aynı değer (API imzalar, gateway doğrular).
    public const string JwtSigningKeyKey = "JWT_SIGNING_KEY";

    private const int MinSecretLength = 32;
    // HMAC-SHA256 anahtarı en az 32 bayt (256 bit); daha kısası kaba kuvvete açık.
    private const int MinSigningKeyBytes = 32;

    private ApiSettings(string gatewaySecret, byte[] jwtSigningKey)
    {
        GatewaySecret = gatewaySecret;
        JwtSigningKey = jwtSigningKey;
    }

    public string GatewaySecret { get; }

    public byte[] JwtSigningKey { get; }

    public static ApiSettings From(IConfiguration configuration)
    {
        var secret = configuration[GatewaySecretKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
            throw Missing(GatewaySecretKey, $"{MinSecretLength} karakterden kısa");

        var signingKey = Encoding.UTF8.GetBytes(configuration[JwtSigningKeyKey] ?? "");
        if (signingKey.Length < MinSigningKeyBytes)
            throw Missing(JwtSigningKeyKey, $"{MinSigningKeyBytes} bayttan kısa");

        return new ApiSettings(secret, signingKey);
    }

    private static InvalidOperationException Missing(string key, string reason) =>
        new($"{key} eksik ya da {reason}. Ortam değişkeni ya da dotnet user-secrets ile ver.");
}
