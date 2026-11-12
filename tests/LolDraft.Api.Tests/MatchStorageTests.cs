using System.Net;
using System.Text;
using LolDraft.Api.Middleware;
using LolDraft.Application;
using LolDraft.Application.Matches;
using LolDraft.Domain;
using LolDraft.Engine;
using LolDraft.Infrastructure;
using LolDraft.Infrastructure.Matches;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LolDraft.Api.Tests;

/// <summary>
/// Kalıcı replay (DESIGN.md §8.16): SQLite dosyası, migration, yeniden başlatma, saklama süresi, ayarlar.
/// Her test kendi geçici veritabanı dosyasıyla çalışır.
/// </summary>
public sealed class MatchStorageTests : IDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private static readonly Instant Now = new(new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.Zero));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lol-draft-tests", Guid.NewGuid().ToString("N"));

    // Alt klasör henüz yok: InitializeAsync klasörü açmalı.
    private string DbPath => Path.Combine(_directory, "data", "matches.db");

    [Fact]
    public async Task Kaydedilen_mac_yeniden_baslatmadan_sonra_aynen_okunur()
    {
        var match = Match(seed: uint.MaxValue);   // seed'in üst sınırı INTEGER sütununa sığar
        await using (var first = await Services())
            await first.GetRequiredService<IMatchRepository>().Save(match, CancellationToken.None);

        // Yeni süreç gibi: yeni DI, yeni bağlantılar, aynı dosya.
        await using var second = await Services();
        var read = await second.GetRequiredService<IMatchRepository>().Get(match.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(match with { Replay = read.Replay }, read);
        Assert.Equal(match.Replay.Version, read.Replay.Version);
        Assert.Equal(match.Replay.Json.ToArray(), read.Replay.Json.ToArray());
    }

    [Fact]
    public async Task Belge_sikistirilmis_yazilir()
    {
        var match = Match();
        await using var services = await Services();
        await services.GetRequiredService<IMatchRepository>().Save(match, CancellationToken.None);

        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT length(Replay) FROM Matches";
        var stored = Convert.ToInt32(await command.ExecuteScalarAsync());

        // Tekrarlı alan adlarıyla dolu JSON en az 5 kat küçülür.
        Assert.True(stored * 5 < match.Replay.Json.Length, $"{stored} bayt, ham {match.Replay.Json.Length}");
    }

    [Fact]
    public async Task Kayit_hatasi_firlatmaz()
    {
        var match = Match();
        await using var services = await Services();
        var matches = services.GetRequiredService<IMatchRepository>();
        await matches.Save(match, CancellationToken.None);

        // Aynı kimlik ikinci kez: birincil anahtar ihlali. Port sözleşmesi: loglanır, yayın ve tik durmaz.
        await matches.Save(match, CancellationToken.None);

        Assert.NotNull(await matches.Get(match.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Saklama_suresini_asan_maclar_silinir()
    {
        var old = Match(playedAt: Now + TimeSpan.FromDays(-181));
        var recent = Match(playedAt: Now + TimeSpan.FromDays(-179));
        await using var services = await Services();
        var matches = services.GetRequiredService<IMatchRepository>();
        await matches.Save(old, CancellationToken.None);
        await matches.Save(recent, CancellationToken.None);

        var deleted = await new PruneMatches(matches, new FixedClock(Now), MatchRetention.Default).Handle(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Null(await matches.Get(old.Id, CancellationToken.None));
        Assert.NotNull(await matches.Get(recent.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Saklama_suresi_sifirsa_hic_silinmez()
    {
        var ancient = Match(playedAt: Now + TimeSpan.FromDays(-3000));
        await using var services = await Services();
        var matches = services.GetRequiredService<IMatchRepository>();
        await matches.Save(ancient, CancellationToken.None);

        var deleted = await new PruneMatches(matches, new FixedClock(Now), new MatchRetention(0)).Handle(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.NotNull(await matches.Get(ancient.Id, CancellationToken.None));
    }

    [Fact]
    public async Task API_yeniden_baslasa_da_tekrar_izleme_linki_calisir_ve_kayitli_baytlari_doner()
    {
        // Şimdiki zamanla: açılışta çalışan saklama süresi silmesi bu maçı atmasın.
        var match = Match(playedAt: new Instant(DateTimeOffset.UtcNow));
        using (var first = Api())
            await first.Services.GetRequiredService<IMatchRepository>().Save(match, CancellationToken.None);

        using var second = Api();
        var client = second.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        var response = await client.GetAsync($"/api/matches/{match.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        // Yeniden serileştirilmez: diskteki belgenin baytları olduğu gibi.
        Assert.Equal(match.Replay.Json.ToArray(), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void Uretimde_veritabani_yolu_zorunlu()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiSettings.From(Config(), isDevelopment: false));
        Assert.Contains("MATCH_DB_PATH", error.Message);

        // Geliştirmede verilmezse bellek.
        Assert.True(ApiSettings.From(Config(), isDevelopment: true).MatchStorage.InMemory);
        Assert.Equal(DbPath, ApiSettings.From(Config(("MATCH_DB_PATH", DbPath)), isDevelopment: false).MatchStorage.FilePath);
    }

    [Theory]
    [InlineData(null, 180)]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    public void Saklama_suresi_ayari(string? value, int expected)
    {
        var settings = ApiSettings.From(Config(("MATCH_RETENTION_DAYS", value)), isDevelopment: true);

        Assert.Equal(expected, settings.MatchRetention.Days);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("on")]
    [InlineData("99999999999")]
    public void Gecersiz_saklama_suresi_uygulamayi_acmaz(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ApiSettings.From(Config(("MATCH_RETENTION_DAYS", value)), isDevelopment: true));
        Assert.Contains("MATCH_RETENTION_DAYS", error.Message);
    }

    public void Dispose()
    {
        // Havuzdaki bağlantılar dosyayı tutar (Windows silmeye izin vermez).
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Geçici klasör; silinemezse işletim sistemi temizler.
        }
    }

    private async Task<ServiceProvider> Services()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(Encoding.UTF8.GetBytes(SigningKey), MatchStorage.File(DbPath))
            .BuildServiceProvider();
        await services.InitializeAsync(CancellationToken.None);
        return services;
    }

    private WebApplicationFactory<GatewayOnlyMiddleware> Api() => new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", Secret)
            .UseSetting("JWT_SIGNING_KEY", SigningKey)
            .UseSetting("MATCH_DB_PATH", DbPath));

    private static IConfiguration Config(params (string Key, string? Value)[] values) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(values.Select(v => KeyValuePair.Create(v.Key, v.Value)))
        {
            ["GATEWAY_SECRET"] = Secret,
            ["JWT_SIGNING_KEY"] = SigningKey,
        })
        .Build();

    // Gerçek bir maç belgesine benzer: tekrarlı alan adlarıyla dolu birkaç yüz olay (sıkıştırma ölçümü için).
    private static MatchRecord Match(uint seed = 7, Instant? playedAt = null)
    {
        var id = MatchId.FromBytes(Guid.NewGuid().ToByteArray());
        var events = string.Join(",", Enumerable.Range(0, 300).Select(i =>
            $$"""{"type":"kill","t":{{i * 7}},"text":"Kill {{i}}","side":"blue","killer":"jinx","victim":"zed","lane":"mid"}"""));
        var json = $$"""{"id":"{{id.Value}}","gameNumber":1,"blueTeam":"A","events":[{{events}}]}""";

        return new MatchRecord(
            id, LobbyCode.FromBytes(Guid.NewGuid().ToByteArray()), 1, TeamSlot.B, new Seed(seed), "abc123",
            playedAt ?? Now, new Replay(Replay.CurrentVersion, Encoding.UTF8.GetBytes(json)));
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant Now => now;
    }
}
