using System.Net;
using LolDraft.Api.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LolDraft.Api.Tests;

/// <summary>API sadece gateway'in sırrını taşıyan isteği kabul eder (DESIGN.md §8.6, GatewayOnlyMiddleware).</summary>
public sealed class GatewayOnlyTests : IDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", Secret).UseSetting("JWT_SIGNING_KEY", SigningKey));

    [Fact]
    public async Task Sirsiz_istek_404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("yanlis-sir")]
    [InlineData("test-gateway-secret-0123456789abcdeX")]   // tek karakter farklı
    [InlineData("test-gateway-secret-0123456789abcde")]    // bir eksik
    public async Task Yanlis_sirla_istek_404(string secret)
    {
        Assert.Equal(HttpStatusCode.NotFound, await Health(secret));
    }

    // İki değer (biri doğru) kabul edilmez: başlık tam olarak bir kez, doğru değerle gelmeli.
    [Fact]
    public async Task Iki_degerli_sir_basligi_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await Health(Secret, "baska"));
    }

    [Fact]
    public async Task Dogru_sirla_istek_gecer()
    {
        Assert.Equal(HttpStatusCode.OK, await Health(Secret));
    }

    [Fact]
    public void Sir_eksik_ya_da_kisaysa_api_acilmaz()
    {
        foreach (var secret in new[] { "", "kisa-sir" })
        {
            using var factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
                .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", secret).UseSetting("JWT_SIGNING_KEY", SigningKey));

            var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("GATEWAY_SECRET", Messages(error));
        }
    }

    [Fact]
    public void Imza_anahtari_eksik_ya_da_kisaysa_api_acilmaz()
    {
        foreach (var key in new[] { "", "kisa-anahtar" })
        {
            using var factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
                .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", Secret).UseSetting("JWT_SIGNING_KEY", key));

            var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("JWT_SIGNING_KEY", Messages(error));
        }
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpStatusCode> Health(params string[] secrets)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(GatewayOnlyMiddleware.HeaderName, secrets);
        return (await _factory.CreateClient().SendAsync(request)).StatusCode;
    }

    private static string Messages(Exception error) =>
        error.InnerException is null ? error.Message : error.Message + " | " + Messages(error.InnerException);
}
