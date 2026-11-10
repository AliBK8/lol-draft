using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LolDraft.Api.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LolDraft.Api.Tests;

/// <summary>Lobi uç noktaları ve misafir token'ı (DESIGN.md §8.4, §8.7). İstekler gateway'den gelmiş gibi sır taşır.</summary>
public sealed class LobbyEndpointsTests : IDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", Secret).UseSetting("JWT_SIGNING_KEY", SigningKey));

    private readonly HttpClient _client;

    public LobbyEndpointsTests()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
    }

    // --- Lobi açma ---

    [Fact]
    public async Task Lobi_acan_A_koltugunu_ve_token_alir()
    {
        var ticket = await Create();

        Assert.Equal("A", ticket.Seat);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", ticket.Code);

        // Token'ın içi: kim (sub), hangi lobi, hangi koltuk; gateway'in beklediği yayıncı ve hedef; 2 saat.
        var jwt = new JsonWebToken(ticket.Token);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", jwt.Subject);
        Assert.Equal(ticket.Code, jwt.GetClaim("lobby").Value);
        Assert.Equal("A", jwt.GetClaim("seat").Value);
        Assert.Equal("lol-draft", jwt.Issuer);
        Assert.Equal(["lol-draft-game"], jwt.Audiences);
        Assert.Equal("HS256", jwt.Alg);
        Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(119), TimeSpan.FromMinutes(121));
    }

    [Fact]
    public async Task Her_lobinin_kodu_ve_oyuncusu_farkli()
    {
        var first = await Create();
        var second = await Create();

        Assert.NotEqual(first.Code, second.Code);
        Assert.NotEqual(new JsonWebToken(first.Token).Subject, new JsonWebToken(second.Token).Subject);
    }

    [Theory]
    [InlineData("bo1", true)]
    [InlineData("bo3", true)]
    [InlineData("bo5", false)]
    public async Task Havuza_sigan_seri_ayarlariyla_lobi_acilir(string format, bool fearless)
    {
        var response = await PostCreate(format, fearless);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Fearless_bo5_40lik_havuza_sigmaz_lobi_acilmaz()
    {
        var response = await PostCreate("bo5", fearless: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pool_too_small", await ErrorCode(response));
    }

    [Theory]
    [InlineData("bo7")]
    [InlineData("BO3")]
    [InlineData("3")]
    [InlineData(null)]
    public async Task Gecersiz_formatla_lobi_acilmaz(string? format)
    {
        var response = await PostCreate(format, fearless: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_format", await ErrorCode(response));
    }

    // --- Açık lobi sınırı (IP başına 3) ---

    [Fact]
    public async Task Ayni_ipden_dorduncu_acik_lobi_429()
    {
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await PostCreateFrom("203.0.113.7")).StatusCode);

        var rejected = await PostCreateFrom("203.0.113.7");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("too_many_lobbies", await ErrorCode(rejected));
        // Başka IP etkilenmez.
        Assert.Equal(HttpStatusCode.OK, (await PostCreateFrom("198.51.100.1")).StatusCode);
    }

    // Aynı anda gelen 10 istekten tam 3'ü lobi açar: sayma ve ekleme tek adım.
    [Fact]
    public async Task Ayni_anda_gelen_isteklerle_sinir_asilmaz()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostCreateFrom("203.0.113.9")));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    // --- Katılma ---

    [Fact]
    public async Task Katilan_B_koltugunu_ve_kendi_tokenini_alir()
    {
        var host = await Create();

        var guest = await Join(host.Code);

        Assert.Equal("B", guest.Seat);
        Assert.Equal(host.Code, guest.Code);
        var jwt = new JsonWebToken(guest.Token);
        Assert.Equal("B", jwt.GetClaim("seat").Value);
        Assert.NotEqual(new JsonWebToken(host.Token).Subject, jwt.Subject);
    }

    [Fact]
    public async Task Dolu_lobiye_katilma_409()
    {
        var host = await Create();
        await Join(host.Code);

        var response = await PostJoin(host.Code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("lobby_full", await ErrorCode(response));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]   // biçim doğru, lobi yok
    [InlineData("bozuk-kod")]
    public async Task Olmayan_lobiye_katilma_404(string code)
    {
        var response = await PostJoin(code);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("lobby_not_found", await ErrorCode(response));
    }

    // Yarış: aynı anda gelen 10 katılmadan tam biri B koltuğunu alır (lobi başına kilit).
    [Fact]
    public async Task Ayni_anda_katilanlardan_sadece_biri_oturur()
    {
        var host = await Create();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostJoin(host.Code)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // --- Lobi durumu ve yetki ---

    [Fact]
    public async Task Oyuncu_kendi_lobisinin_durumunu_gorur()
    {
        var host = await Create("bo3", fearless: true);

        var before = await GetLobby(host.Code, host.Token);
        var guest = await Join(host.Code);
        var after = await GetLobby(host.Code, guest.Token);

        Assert.Equal("A", before.YourSeat);
        Assert.Equal(new LobbyState(host.Code, "bo3", true, false, 1, before.Lobby.BlueTeam, 0, 0, null), before.Lobby);
        Assert.Contains(before.Lobby.BlueTeam, new[] { "A", "B" });
        Assert.Equal("B", after.YourSeat);
        Assert.Equal(before.Lobby with { IsFull = true }, after.Lobby);
    }

    [Fact]
    public async Task Tokensiz_ya_da_bozuk_tokenla_lobi_gorulmez()
    {
        var host = await Create();
        // İmzanın son karakterini değiştir: içerik aynı, imza tutmaz.
        var tampered = host.Token[..^1] + (host.Token[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendGet(host.Code, token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendGet(host.Code, "bu-bir-token-degil")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendGet(host.Code, tampered)).StatusCode);
    }

    [Fact]
    public async Task Baska_anahtarla_imzalanmis_token_kabul_edilmez()
    {
        var host = await Create();
        var real = new JsonWebToken(host.Token);
        var forged = Forge("baska-bir-anahtar-0123456789abcdefghijklmn", real.Subject, host.Code, "A");

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendGet(host.Code, forged)).StatusCode);
    }

    [Fact]
    public async Task Baska_lobinin_tokeniyla_lobi_gorulmez()
    {
        var mine = await Create();
        var other = await Create();

        var response = await SendGet(other.Code, mine.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_in_lobby", await ErrorCode(response));
    }

    // İmza doğru olsa da son söz lobinin: token'daki oyuncu o koltukta oturmuyorsa 403.
    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA", "A")]   // lobide olmayan oyuncu
    [InlineData(null, "B")]                        // gerçek oyuncu, yanlış koltuk
    public async Task Lobide_o_koltukta_oturmayan_oyuncu_goremez(string? player, string seat)
    {
        var host = await Create();
        var token = Forge(SigningKey, player ?? new JsonWebToken(host.Token).Subject, host.Code, seat);

        var response = await SendGet(host.Code, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private Task<HttpResponseMessage> PostCreate(string? format, bool fearless) =>
        _client.PostAsJsonAsync("/api/lobbies", new { format, fearless });

    // Gateway'in yazacağı istemci IP başlığıyla (API testleri gateway'den gelmiş gibi istek atar).
    private Task<HttpResponseMessage> PostCreateFrom(string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/lobbies")
        {
            Content = JsonContent.Create(new { format = "bo1", fearless = false }),
        };
        request.Headers.Add(GatewayOnlyMiddleware.ClientIpHeader, ip);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostJoin(string code) => _client.PostAsync($"/api/lobbies/{code}/join", null);

    private async Task<Ticket> Create(string format = "bo3", bool fearless = false) =>
        await Read<Ticket>(await PostCreate(format, fearless));

    private async Task<Ticket> Join(string code) => await Read<Ticket>(await PostJoin(code));

    private async Task<LobbyResponse> GetLobby(string code, string token) => await Read<LobbyResponse>(await SendGet(code, token));

    private Task<HttpResponseMessage> SendGet(string code, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/lobbies/{code}");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error;

    private static string Forge(string key, string player, string lobby, string seat) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "lol-draft",
            Audience = "lol-draft-game",
            Claims = new Dictionary<string, object> { ["sub"] = player, ["lobby"] = lobby, ["seat"] = seat },
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });

    private sealed record Ticket(string Code, string Seat, string Token);

    private sealed record LobbyResponse(string YourSeat, LobbyState Lobby);

    // Taslak (draft) ve saat alanları burada okunmaz; JSON'daki fazla alanlar yok sayılır.
    private sealed record LobbyState(
        string Code, string Format, bool Fearless, bool IsFull, int GameNumber, string BlueTeam, int WinsA, int WinsB, object? Draft);

    private sealed record ErrorBody(string Error, string Message);
}
