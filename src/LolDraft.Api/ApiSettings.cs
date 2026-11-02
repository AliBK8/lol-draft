namespace LolDraft.Api;

/// <summary>
/// API'nin sırları (DESIGN.md §8.6): ortam değişkeninden (geliştirmede dotnet user-secrets) okunur, repoya girmez.
/// Eksik ya da zayıfsa uygulama açılmaz (fail fast): sırsız çalışan bir API, gateway'i atlatan isteği ayırt edemez.
/// </summary>
internal sealed record ApiSettings
{
    // Gateway'le paylaşılan sır, en az 32 karakter (rastgele üretilmiş; ör. openssl rand -base64 48).
    public const string GatewaySecretKey = "GATEWAY_SECRET";
    private const int MinSecretLength = 32;

    private ApiSettings(string gatewaySecret) => GatewaySecret = gatewaySecret;

    public string GatewaySecret { get; }

    public static ApiSettings From(IConfiguration configuration)
    {
        var secret = configuration[GatewaySecretKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
        {
            throw new InvalidOperationException(
                $"{GatewaySecretKey} eksik ya da {MinSecretLength} karakterden kısa. Ortam değişkeni ya da dotnet user-secrets ile ver.");
        }

        return new ApiSettings(secret);
    }
}
