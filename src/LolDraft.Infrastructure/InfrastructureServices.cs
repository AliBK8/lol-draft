using System.Security.Cryptography;
using LolDraft.Application;
using LolDraft.Domain;
using LolDraft.Infrastructure.Auth;
using LolDraft.Infrastructure.Champions;
using LolDraft.Infrastructure.Lobbies;
using LolDraft.Infrastructure.Matches;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LolDraft.Infrastructure;

/// <summary>Infrastructure'ın dışa açık tek kapısı: portların gerçeklerini kaydeder (sınıflar internal kalır).</summary>
public static class InfrastructureServices
{
    /// <param name="jwtSigningKey">Misafir JWT'sinin HMAC-SHA256 anahtarı (gateway ile aynı değer).</param>
    /// <param name="matchStorage">Oynanmış maçların veritabanı (dosya ya da geliştirme/test için bellek).</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, byte[] jwtSigningKey, MatchStorage matchStorage)
    {
        ArgumentNullException.ThrowIfNull(matchStorage);

        // Hepsi singleton: lobi deposu uygulama boyunca tek olmalı, diğerleri durumsuz.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecureRandom, CryptoRandom>();
        services.AddSingleton<IDraftRandom, CryptoDraftRandom>();
        services.AddSingleton<IChampionCatalog, EmbeddedChampionCatalog>();
        services.AddSingleton<ILobbyRepository, InMemoryLobbyRepository>();
        services.AddSingleton<IMatchRepository, SqliteMatchRepository>();
        services.AddSingleton(matchStorage);
        // Bellekteki veritabanı son bağlantı kapanınca silinir: açık tutan "çapa" bağlantı her context'ten önce hazır.
        services.AddSingleton<MemoryAnchor>();
        services.AddDbContextFactory<MatchDbContext>((provider, options) =>
        {
            if (matchStorage.InMemory)
                provider.GetRequiredService<MemoryAnchor>();
            options.UseSqlite(matchStorage.ConnectionString);
        });
        services.AddSingleton<IGuestTokenService>(provider =>
            new GuestTokenService(jwtSigningKey, provider.GetRequiredService<IClock>()));
        return services;
    }

    /// <summary>
    /// Uygulama açılmadan önce bir kez: veritabanı klasörünü açar, bekleyen migration'ları uygular, dosyada WAL'a geçer.
    /// Şema uyuşmazlığında uygulama açılmaz (yarım şemayla çalışmak sessiz veri kaybı olurdu).
    /// </summary>
    public static async Task InitializeAsync(this IServiceProvider services, CancellationToken ct)
    {
        var storage = services.GetRequiredService<MatchStorage>();
        if (storage.FilePath is { } path && Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
            Directory.CreateDirectory(directory);

        var contexts = services.GetRequiredService<IDbContextFactory<MatchDbContext>>();
        await using var db = await contexts.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);

        // WAL: okuma (replay) yazmayı (yeni maç) beklemez. Dosyaya kalıcı yazılır, her açılışta tekrarı zararsız.
        // Sabit PRAGMA; kullanıcı girdisi yok (ham SQL yasağı girdili sorgular için, DESIGN.md §8.6).
        if (!storage.InMemory)
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }

    // Bellekteki paylaşılan veritabanını yaşatan tek açık bağlantı; DI kapanınca kapanır, veritabanı da gider.
    private sealed class MemoryAnchor : IDisposable
    {
        private readonly SqliteConnection _connection;

        public MemoryAnchor(MatchStorage storage)
        {
            _connection = new SqliteConnection(storage.ConnectionString);
            _connection.Open();
        }

        public void Dispose() => _connection.Dispose();
    }

    private sealed class SystemClock : IClock
    {
        public Instant Now => new(DateTimeOffset.UtcNow);
    }

    // İşletim sisteminin CSPRNG'si; System.Random tahmin edilebilir, kod ve kimlik için kullanılmaz.
    private sealed class CryptoRandom : ISecureRandom
    {
        public byte[] Bytes(int count) => RandomNumberGenerator.GetBytes(count);
    }

    // Zaman aşımındaki rastgele pick: tahmin edilemez olmalı (yoksa oyuncu "süreyi bitirirsem X gelir" diye oynar).
    private sealed class CryptoDraftRandom : IDraftRandom
    {
        public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
    }
}
