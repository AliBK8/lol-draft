using System.Security.Cryptography;
using LolDraft.Application;
using LolDraft.Domain;
using LolDraft.Infrastructure.Auth;
using LolDraft.Infrastructure.Champions;
using LolDraft.Infrastructure.Lobbies;
using LolDraft.Infrastructure.Matches;
using Microsoft.Extensions.DependencyInjection;

namespace LolDraft.Infrastructure;

/// <summary>Infrastructure'ın dışa açık tek kapısı: portların gerçeklerini kaydeder (sınıflar internal kalır).</summary>
public static class InfrastructureServices
{
    /// <param name="jwtSigningKey">Misafir JWT'sinin HMAC-SHA256 anahtarı (gateway ile aynı değer).</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, byte[] jwtSigningKey)
    {
        // Hepsi singleton: lobi deposu uygulama boyunca tek olmalı, diğerleri durumsuz.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecureRandom, CryptoRandom>();
        services.AddSingleton<IDraftRandom, CryptoDraftRandom>();
        services.AddSingleton<IChampionCatalog, EmbeddedChampionCatalog>();
        services.AddSingleton<ILobbyRepository, InMemoryLobbyRepository>();
        services.AddSingleton<IMatchRepository, InMemoryMatchRepository>();
        services.AddSingleton<IGuestTokenService>(provider =>
            new GuestTokenService(jwtSigningKey, provider.GetRequiredService<IClock>()));
        return services;
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
