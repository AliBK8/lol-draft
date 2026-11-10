using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LolDraft.Gateway.Tests;

/// <summary>
/// Uçtan uca lobi akışı, gerçek hattan (gateway → YARP → API): API'nin ürettiği token'ı gateway kabul ediyor mu?
/// İki uygulama anahtarı, yayıncıyı, hedefi ve claim adlarını ayrı ayrı tanımlar; uyuşmazlık burada yakalanır.
/// </summary>
public sealed class LobbyFlowTests : IDisposable
{
    private readonly GatewayPipeline _pipeline = new();
    private readonly HttpClient _client;

    public LobbyFlowTests() => _client = _pipeline.CreateClient();

    [Fact]
    public async Task Lobi_ac_katil_ve_tokenla_durumu_oku()
    {
        // Ali lobi açar.
        var created = await _client.PostAsJsonAsync("/api/lobbies", new { format = "bo3", fearless = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var host = (await created.Content.ReadFromJsonAsync<Ticket>())!;
        Assert.Equal("A", host.Seat);

        // Veli linkle katılır.
        var joined = await _client.PostAsync($"/api/lobbies/{host.Code}/join", null);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var guest = (await joined.Content.ReadFromJsonAsync<Ticket>())!;
        Assert.Equal("B", guest.Seat);

        // İkisi de kendi token'ıyla lobiyi görür: gateway imzayı doğrular, API yetkiyi.
        foreach (var ticket in new[] { host, guest })
        {
            var response = await Get(host.Code, ticket.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var state = (await response.Content.ReadFromJsonAsync<LobbyState>())!;
            Assert.Equal(ticket.Seat, state.YourSeat);
            Assert.True(state.IsFull);
        }
    }

    [Fact]
    public async Task Tokensiz_lobi_istegi_gatewayde_durur()
    {
        var created = await _client.PostAsJsonAsync("/api/lobbies", new { format = "bo1", fearless = false });
        var host = (await created.Content.ReadFromJsonAsync<Ticket>())!;
        var path = $"/api/lobbies/{host.Code}";

        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(_pipeline.Probe.Reached(path));
    }

    public void Dispose() => _pipeline.Dispose();

    private Task<HttpResponseMessage> Get(string code, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/lobbies/{code}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private sealed record Ticket(string Code, string Seat, string Token);

    private sealed record LobbyState(string YourSeat, bool IsFull);
}
