using System.Net.Http.Json;
using System.Text.Json;
using LolDraft.Api.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LolDraft.Api.Tests;

/// <summary>Şampiyon listesi (DESIGN.md §8.17): istemcinin draft ve swap ekranı için bildiği her şey, fazlası değil.</summary>
public sealed class ChampionEndpointsTests : IDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", Secret)
            .UseSetting("JWT_SIGNING_KEY", "test-jwt-signing-key-0123456789abcdefghij"));

    [Fact]
    public async Task Kirk_sampiyon_ad_rol_ve_yuva_basina_rol_disi_ceza_ile_gelir()
    {
        var (response, champions) = await Get();

        Assert.Equal(40, champions.Count);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());

        foreach (var champion in champions)
        {
            Assert.Matches("^[a-z]+$", champion.GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(champion.GetProperty("name").GetString()));
            var roles = champion.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToList();
            Assert.NotEmpty(roles);

            // Beş yuvanın hepsi; kendi rollerinde 0, diğerlerinde tablodaki değerlerden biri (§16.8).
            var offRole = champion.GetProperty("offRole");
            Assert.Equal(["top", "jungle", "mid", "bot", "support"], offRole.EnumerateObject().Select(p => p.Name));
            foreach (var slot in offRole.EnumerateObject())
            {
                var penalty = slot.Value.GetDouble();
                if (roles.Contains(slot.Name))
                    Assert.Equal(0, penalty);
                else
                    Assert.Contains(penalty, new[] { 0.20, 0.25, 0.30, 0.35 });
            }
        }
    }

    [Fact]
    public async Task Ornek_ceza_tablosu_ile_ayni()
    {
        var (_, champions) = await Get();

        // Tek rollü bir support: top'ta %35, mid'de %30 (tablo motorda, istemciye hesaplanmış gider).
        var support = champions.First(c =>
            c.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).SequenceEqual(["support"]));
        Assert.Equal(0.35, support.GetProperty("offRole").GetProperty("top").GetDouble());
        Assert.Equal(0.30, support.GetProperty("offRole").GetProperty("mid").GetDouble());
    }

    [Fact]
    public async Task Gizli_motor_sayilari_disari_cikmaz()
    {
        var (_, champions) = await Get();

        // §0 "matematik gizli": sadece id, ad, roller, rol dışı ceza.
        Assert.All(champions, c =>
            Assert.Equal(["id", "name", "roles", "offRole"], c.EnumerateObject().Select(p => p.Name)));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpResponseMessage Response, List<JsonElement> Champions)> Get()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        var response = await client.GetAsync("/api/champions");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response, body.GetProperty("champions").EnumerateArray().ToList());
    }
}
