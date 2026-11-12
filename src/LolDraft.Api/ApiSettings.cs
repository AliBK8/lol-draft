using System.Globalization;
using System.Text;
using LolDraft.Application.Matches;
using LolDraft.Infrastructure.Matches;

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
    // Maç veritabanının dosya yolu (Docker'da volume içi, ör. /data/matches.db). Sır değil, ayar.
    public const string MatchDbPathKey = "MATCH_DB_PATH";
    // Replay saklama süresi (gün); 0 = hiç silme. Verilmezse 180.
    public const string MatchRetentionDaysKey = "MATCH_RETENTION_DAYS";

    private const int MinSecretLength = 32;
    // HMAC-SHA256 anahtarı en az 32 bayt (256 bit); daha kısası kaba kuvvete açık.
    private const int MinSigningKeyBytes = 32;

    private ApiSettings(string gatewaySecret, byte[] jwtSigningKey, MatchStorage matchStorage, MatchRetention matchRetention)
    {
        GatewaySecret = gatewaySecret;
        JwtSigningKey = jwtSigningKey;
        MatchStorage = matchStorage;
        MatchRetention = matchRetention;
    }

    public string GatewaySecret { get; }

    public byte[] JwtSigningKey { get; }

    public MatchStorage MatchStorage { get; }

    public MatchRetention MatchRetention { get; }

    /// <param name="isDevelopment">
    /// Geliştirmede (ve testlerde) <see cref="MatchDbPathKey"/> verilmezse maçlar bellekte tutulur; üretimde zorunlu:
    /// yolu unutulmuş sunucu replay'leri sessizce kaybetmesin.
    /// </param>
    public static ApiSettings From(IConfiguration configuration, bool isDevelopment)
    {
        var secret = configuration[GatewaySecretKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
            throw Missing(GatewaySecretKey, $"{MinSecretLength} karakterden kısa");

        var signingKey = Encoding.UTF8.GetBytes(configuration[JwtSigningKeyKey] ?? "");
        if (signingKey.Length < MinSigningKeyBytes)
            throw Missing(JwtSigningKeyKey, $"{MinSigningKeyBytes} bayttan kısa");

        var dbPath = configuration[MatchDbPathKey];
        MatchStorage storage;
        if (!string.IsNullOrWhiteSpace(dbPath))
            storage = MatchStorage.File(dbPath);
        else if (isDevelopment)
            storage = MatchStorage.Memory();
        else
            throw new InvalidOperationException($"{MatchDbPathKey} eksik. Üretimde maç veritabanının yolu zorunlu (ör. /data/matches.db).");

        var retention = MatchRetention.Default;
        if (configuration[MatchRetentionDaysKey] is { Length: > 0 } days)
        {
            if (!int.TryParse(days, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidOperationException($"{MatchRetentionDaysKey} 0 ya da pozitif bir gün sayısı olmalı.");
            retention = new MatchRetention(parsed);
        }

        return new ApiSettings(secret, signingKey, storage, retention);
    }

    private static InvalidOperationException Missing(string key, string reason) =>
        new($"{key} eksik ya da {reason}. Ortam değişkeni ya da dotnet user-secrets ile ver.");
}
