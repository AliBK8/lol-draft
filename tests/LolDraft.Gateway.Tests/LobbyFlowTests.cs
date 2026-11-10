using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

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
            var state = (await response.Content.ReadFromJsonAsync<LobbyResponse>())!;
            Assert.Equal(ticket.Seat, state.YourSeat);
            Assert.True(state.Lobby.IsFull);
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

    // Hub gateway'den geçer: YARP rotası (/hubs/draft/{**rest}), gateway'in JWT kontrolü ve API'nin kendi
    // doğrulaması birlikte çalışıyor mu? İki oyuncu bağlanır, hazır der, draft başlar.
    [Fact]
    public async Task Hub_gateway_uzerinden_calisir_ve_draft_baslar()
    {
        var created = await _client.PostAsJsonAsync("/api/lobbies", new { format = "bo1", fearless = false });
        var host = (await created.Content.ReadFromJsonAsync<Ticket>())!;
        var guest = (await (await _client.PostAsync($"/api/lobbies/{host.Code}/join", null))
            .Content.ReadFromJsonAsync<Ticket>())!;

        var phases = new ConcurrentQueue<string?>();
        await using var hostConnection = await ConnectHub(host.Token, phases);
        await using var guestConnection = await ConnectHub(guest.Token, new());

        var first = await hostConnection.InvokeAsync<HubResult>("Ready", Guid.NewGuid());
        var second = await guestConnection.InvokeAsync<HubResult>("Ready", Guid.NewGuid());

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        await WaitUntil(() => phases.Contains("ban1"));
    }

    [Fact]
    public async Task Tokensiz_hub_baglantisi_gatewayde_durur()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_pipeline.BaseAddress, "hubs/draft"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _pipeline.CreateHandler();
            })
            .Build();
        await using var _ = connection;

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.DoesNotContain(_pipeline.Probe.Requests, r => r.Path.StartsWith("/hubs"));
    }

    // İstemci IP başlığını taklit ederek açık lobi sınırını atlatamaz: gateway onu siler, kendi gördüğünü yazar.
    [Fact]
    public async Task Sahte_istemci_ip_basligi_lobi_sinirini_atlatamaz()
    {
        var statuses = new List<HttpStatusCode>();
        foreach (var fake in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3", "4.4.4.4" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/lobbies")
            {
                Content = JsonContent.Create(new { format = "bo1", fearless = false }),
            };
            request.Headers.Add("X-Client-Ip", fake);
            request.Headers.Add("X-Forwarded-For", fake);
            statuses.Add((await _client.SendAsync(request)).StatusCode);
        }

        // API her istekte tek değer gördü: gateway'in gördüğü bağlantı adresi, istemcinin yazdığı değil.
        Assert.All(_pipeline.Probe.ClientIps, values => Assert.Equal([GatewayPipeline.ClientIp], values));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    // Regresyon (4c'den kalma açık): güvenilen proxy listesi boşken ForwardedHeaders herkese güveniyordu; istemci
    // her istekte farklı X-Forwarded-For yazarak IP tabanlı rate limit'i (lobi açma dakikada 5) atlatabiliyordu.
    [Fact]
    public async Task Sahte_forwarded_for_ile_gateway_rate_limiti_atlatilamaz()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/lobbies");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{i}");
            statuses.Add((await _client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }

    // Güvenilen bir proxy tanımlıysa (ör. bulut yük dengeleyicisi) onun X-Forwarded-For'u kullanılır.
    [Fact]
    public async Task Guvenilen_proxyden_gelen_forwarded_for_kullanilir()
    {
        using var pipeline = new GatewayPipeline(trustedProxy: GatewayPipeline.ClientIp);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Forwarded-For", "198.51.100.23");

        await pipeline.CreateClient().SendAsync(request);

        Assert.Equal(["198.51.100.23"], Assert.Single(pipeline.Probe.ClientIps));
    }

    public void Dispose() => _pipeline.Dispose();

    private async Task<HubConnection> ConnectHub(
        string token, ConcurrentQueue<string?> phases)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_pipeline.BaseAddress, "hubs/draft"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _pipeline.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        connection.On<JsonElement>(
            "LobbyUpdated", state =>
                phases.Enqueue(state.GetProperty("draft") is { ValueKind: JsonValueKind.Object } draft
                    ? draft.GetProperty("phase").GetString()
                    : null));

        await connection.StartAsync();
        return connection;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Beklenen olay 5 sn içinde gelmedi.");
            await Task.Delay(20);
        }
    }

    private sealed record HubResult(bool Ok, string? Error);

    private Task<HttpResponseMessage> Get(string code, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/lobbies/{code}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private sealed record Ticket(string Code, string Seat, string Token);

    private sealed record LobbyResponse(string YourSeat, LobbyState Lobby);

    private sealed record LobbyState(bool IsFull);
}
