using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using LolDraft.Gateway.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace LolDraft.Gateway.Tests;

/// <summary>Gateway → API hattı ve gateway'in önlemleri (DESIGN.md §8.1, §8.4-§8.6).</summary>
public sealed class GatewayTests : IDisposable
{
    private readonly GatewayPipeline _pipeline = new();
    private readonly HttpClient _client;

    public GatewayTests() => _client = _pipeline.CreateClient();

    // --- Yönlendirme ve paylaşılan sır ---

    [Fact]
    public async Task Gateway_istegi_sirla_birlikte_apiye_iletir()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, secrets) = Assert.Single(_pipeline.Probe.Requests);
        Assert.Equal([GatewayPipeline.Secret], secrets);
    }

    // İstemci sır başlığı gönderirse silinir: API sadece gateway'in değerini görür (tek değer).
    [Fact]
    public async Task Istemcinin_gonderdigi_sir_basligi_silinir()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Gateway-Secret", "sahte-sir");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([GatewayPipeline.Secret], Assert.Single(_pipeline.Probe.Requests).Secrets);
    }

    // Beyaz liste: appsettings.json'da olmayan yol gateway'de 404; bilinen yola yanlış metot 405. İkisi de API'ye
    // hiç ulaşmaz.
    [Theory]
    [InlineData("GET", "/api/admin", HttpStatusCode.NotFound)]
    [InlineData("GET", "/internal/metrics", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/api/lobbies/abc", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/api/lobbies", HttpStatusCode.MethodNotAllowed)]
    public async Task Beyaz_liste_disindaki_istek_apiye_ulasmaz(string method, string path, HttpStatusCode expected)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(_pipeline.Probe.Requests);
    }

    // --- Güvenlik başlıkları ---

    [Fact]
    public async Task Cevapta_guvenlik_basliklari_var_server_basligi_yok()
    {
        var response = await _client.GetAsync("/api/health");
        var headers = response.Headers;

        Assert.Contains("frame-ancestors 'none'", headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", headers.GetValues("X-Frame-Options").Single());
        Assert.False(headers.Contains("Server"));
    }

    // --- Gövde sınırı ---

    [Fact]
    public async Task Buyuk_govde_413_ve_apiye_ulasmaz()
    {
        var body = new ByteArrayContent(new byte[RequestSizeLimitMiddleware.MaxBodyBytes + 1]);

        var response = await _client.PostAsync("/api/lobbies", body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_pipeline.Probe.Requests);
    }

    // --- Rate limiting ---

    // Lobi oluşturma dakikada 5: 6. istek 429 + Retry-After, API'ye ulaşmaz.
    [Fact]
    public async Task Lobi_olusturma_dakikada_5()
    {
        for (var i = 0; i < 5; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await _client.PostAsync("/api/lobbies", null)).StatusCode);

        var rejected = await _client.PostAsync("/api/lobbies", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(5, _pipeline.Probe.Requests.Count);
    }

    // Politikalar birbirinden bağımsız: lobi oluşturma sınırı dolunca sağlık kontrolü (sadece global) geçer.
    [Fact]
    public async Task Rota_siniri_digerlerini_etkilemez()
    {
        for (var i = 0; i < 6; i++)
            await _client.PostAsync("/api/lobbies", null);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/health")).StatusCode);
    }

    // --- JWT ---

    [Fact]
    public async Task Korumali_rota_tokensiz_401_ve_apiye_ulasmaz()
    {
        var response = await _client.GetAsync("/api/lobbies/lobby-code");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_pipeline.Probe.Requests);
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "baska-anahtar", GatewayPipeline.Token(key: "baska-bir-imza-anahtari-0123456789abcdef") },
        { "suresi-dolmus", GatewayPipeline.Token(expiresIn: TimeSpan.FromMinutes(-5)) },
        { "baska-yayinci", GatewayPipeline.Token(issuer: "baska-uygulama") },
        { "imzasiz", Unsigned() },
        { "bozuk", "bu.bir.token.degil" },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Gecersiz_token_401(string why, string token)
    {
        var response = await Get("/api/lobbies/lobby-code", token);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{why}: {response.StatusCode}");
        Assert.Empty(_pipeline.Probe.Requests);
    }

    // İmza geçerli ama lobi/koltuk bilgisi yok: kimlik doğru, yetki yok → 403.
    [Fact]
    public async Task Lobi_bilgisi_olmayan_token_403()
    {
        var response = await Get("/api/lobbies/lobby-code", GatewayPipeline.Token(withLobby: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_pipeline.Probe.Requests);
    }

    [Fact]
    public async Task Gecerli_token_apiye_ulasir()
    {
        await Get("/api/lobbies/lobby-code", GatewayPipeline.Token());

        Assert.True(_pipeline.Probe.Reached("/api/lobbies/lobby-code"));
    }

    // Hub: WebSocket başlık gönderemez; token access_token sorgusundan okunur. Diğer yollarda sorgu kabul edilmez.
    [Fact]
    public async Task Hub_tokeni_sorgudan_okunur_diger_yollarda_okunmaz()
    {
        var token = GatewayPipeline.Token();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/hubs/draft/negotiate", null)).StatusCode);
        await _client.PostAsync($"/hubs/draft/negotiate?access_token={token}", null);
        Assert.True(_pipeline.Probe.Reached($"/hubs/draft/negotiate?access_token={token}"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync($"/api/lobbies/lobby-code?access_token={token}")).StatusCode);
    }

    // --- Correlation id ---

    [Fact]
    public async Task Gecerli_correlation_id_korunur_gecersizi_degistirilir()
    {
        Assert.Equal("istek-42", await CorrelationId("istek-42"));

        var replaced = await CorrelationId("<script>alert(1)</script>");
        Assert.Matches(new Regex("^[a-f0-9]{32}$"), replaced);
        Assert.Matches(new Regex("^[a-f0-9]{32}$"), await CorrelationId(null));
    }

    // --- Başlangıç ---

    [Fact]
    public void Sirlar_eksik_ya_da_zayifsa_gateway_acilmaz()
    {
        Assert.Contains("JWT_SIGNING_KEY", StartupError("JWT_SIGNING_KEY", "kisa-anahtar"));
        Assert.Contains("GATEWAY_SECRET", StartupError("GATEWAY_SECRET", ""));
    }

    public void Dispose()
    {
        _client.Dispose();
        _pipeline.Dispose();
    }

    private Task<HttpResponseMessage> Get(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private async Task<string> CorrelationId(string? incoming)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        if (incoming is not null)
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, incoming);
        var response = await _client.SendAsync(request);
        return response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
    }

    // "alg": "none": imzasız token (algoritma karıştırma saldırısı).
    private static string Unsigned()
    {
        static string Part(string json) => Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(json);
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        return Part("""{"alg":"none","typ":"JWT"}""") + "." +
               Part($$"""{"sub":"p","lobby":"l","seat":"Blue","iss":"lol-draft","aud":"lol-draft-game","exp":{{exp}}}""") + ".";
    }

    private static string StartupError(string key, string value)
    {
        using var factory = new WebApplicationFactory<CorrelationIdMiddleware>().WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", GatewayPipeline.Secret)
            .UseSetting("JWT_SIGNING_KEY", GatewayPipeline.SigningKey)
            .UseSetting(key, value));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var messages = new List<string>();
        for (var e = error; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }
}
