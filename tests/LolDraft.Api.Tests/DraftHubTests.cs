using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using LolDraft.Api.Middleware;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace LolDraft.Api.Tests;

/// <summary>
/// Canlı draft hub'ı (DESIGN.md §8.7), gerçek SignalR istemcisiyle. Bağlantı long polling: test sunucusu
/// WebSocket'i ayrıca kurmayı ister, mesajlaşma ve yetki iki taşımada da aynı.
/// </summary>
public sealed class DraftHubTests : IAsyncDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", Secret).UseSetting("JWT_SIGNING_KEY", SigningKey));

    private readonly List<HubConnection> _connections = [];

    [Fact]
    public async Task Baglaninca_lobinin_guncel_durumu_gelir()
    {
        var host = await CreateLobby();
        var player = await Connect(host.Token);

        var state = await player.Next();

        Assert.Equal(host.Code, state.GetProperty("code").GetString());
        Assert.False(state.GetProperty("isFull").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Iki_oyuncu_hazir_olunca_draft_baslar_ve_ikisine_de_yayinlanir()
    {
        var (host, guest) = await TwoPlayers();

        Assert.True((await host.Invoke("Ready")).Ok);
        var afterHostReady = await guest.Next();
        Assert.Equal(["A"], Strings(afterHostReady.GetProperty("ready")));

        Assert.True((await guest.Invoke("Ready")).Ok);
        await host.Next();   // host'un kendi hazırı
        var started = await host.Next();

        var draft = started.GetProperty("draft");
        Assert.Equal("ban1", draft.GetProperty("phase").GetString());
        Assert.Equal("blue", draft.GetProperty("turn").GetString());
        Assert.Equal("ban", draft.GetProperty("step").GetString());

        // Geri sayım: bitiş anı − sunucu saati = 30 sn.
        var deadline = draft.GetProperty("deadline").GetDateTimeOffset();
        var serverNow = started.GetProperty("serverNow").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromSeconds(30), deadline - serverNow);
    }

    [Fact]
    public async Task Sirasi_gelen_ban_lar_rakip_aninda_gorur()
    {
        var (_, _, blue, red) = await StartedDraft();

        var result = await blue.Invoke("Ban", "zed");

        Assert.True(result.Ok);
        var seenByRed = await red.Last();
        var action = seenByRed.GetProperty("draft").GetProperty("actions")[0];
        Assert.Equal("blue", action.GetProperty("side").GetString());
        Assert.Equal("ban", action.GetProperty("kind").GetString());
        Assert.Equal("zed", action.GetProperty("champion").GetString());
        Assert.Equal("red", seenByRed.GetProperty("draft").GetProperty("turn").GetString());
    }

    [Fact]
    public async Task Sirasi_olmayanin_hamlesi_reddedilir_ve_yayinlanmaz()
    {
        var (_, _, blue, red) = await StartedDraft();
        var before = blue.Received;

        var result = await red.Invoke("Ban", "zed");

        Assert.False(result.Ok);
        Assert.Equal("not_your_turn", result.Error);
        await Task.Delay(100);
        Assert.Equal(before, blue.Received);
    }

    [Theory]
    [InlineData("Zed", "invalid_champion_id")]
    [InlineData("olmayan", "not_in_pool")]
    public async Task Gecersiz_sampiyon_reddedilir(string champion, string error)
    {
        var (_, _, blue, _) = await StartedDraft();

        var result = await blue.Invoke("Ban", champion);

        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task Ayni_hamle_kimligi_iki_kez_gelirse_bir_kez_uygulanir()
    {
        var (_, _, blue, _) = await StartedDraft();
        var actionId = Guid.NewGuid();

        Assert.True((await blue.Invoke("Ban", "zed", actionId)).Ok);
        Assert.True((await blue.Invoke("Ban", "zed", actionId)).Ok);

        var actions = (await blue.Last()).GetProperty("draft").GetProperty("actions");
        Assert.Equal(1, actions.GetArrayLength());
    }

    [Fact]
    public async Task Bos_hamle_kimligi_reddedilir()
    {
        var (_, _, blue, _) = await StartedDraft();

        Assert.Equal("invalid_action_id", (await blue.Invoke("Ban", "zed", Guid.Empty)).Error);
    }

    [Fact]
    public async Task Tokensiz_baglanti_kurulamaz()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Connect(token: null).AsTask());
    }

    [Fact]
    public async Task Rakip_baglaninca_ve_kopunca_ev_sahibi_gorur()
    {
        var host = await CreateLobby();
        var guestTicket = await JoinLobby(host.Code);
        var hostPlayer = await Connect(host.Token);
        await hostPlayer.Last();

        var guest = await Connect(guestTicket.Token);
        var connected = await hostPlayer.Last();
        Assert.True(Seat(connected, "B").GetProperty("connected").GetBoolean());

        await guest.Disconnect();
        var left = await hostPlayer.Next();
        var seat = Seat(left, "B");
        Assert.False(seat.GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.String, seat.GetProperty("absentSince").ValueKind);
    }

    [Fact]
    public async Task Bozuk_tokenla_baglanti_kurulamaz()
    {
        var host = await CreateLobby();
        var tampered = host.Token[..^1] + (host.Token[^1] == 'A' ? 'B' : 'A');

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Connect(tampered).AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // --- Yardımcılar ---

    private async Task<(Player Host, Player Guest)> TwoPlayers()
    {
        var ticket = await CreateLobby();
        var guestTicket = await JoinLobby(ticket.Code);
        var host = await Connect(ticket.Token);
        var guest = await Connect(guestTicket.Token);
        // Bağlanma herkese yayınlanır (rakip "bağlandı" görür): ev sahibi iki, misafir bir olay alır. Kuyruklar boşaltılır.
        await host.Last();
        await guest.Last();
        return (host, guest);
    }

    /// <summary>Draft başlamış lobi; mavi ve kırmızı oyuncu serinin yazı turasına göre.</summary>
    private async Task<(Player Host, Player Guest, Player Blue, Player Red)> StartedDraft()
    {
        var (host, guest) = await TwoPlayers();
        await host.Invoke("Ready");
        await guest.Invoke("Ready");

        var state = await host.Last();
        var hostIsBlue = state.GetProperty("blueTeam").GetString() == "A";
        await guest.Last();
        return hostIsBlue ? (host, guest, host, guest) : (host, guest, guest, host);
    }

    private async Task<Ticket> CreateLobby()
    {
        var response = await Client().PostAsJsonAsync("/api/lobbies", new { format = "bo3", fearless = false });
        return (await response.Content.ReadFromJsonAsync<Ticket>())!;
    }

    private async Task<Ticket> JoinLobby(string code)
    {
        var response = await Client().PostAsync($"/api/lobbies/{code}/join", null);
        return (await response.Content.ReadFromJsonAsync<Ticket>())!;
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        return client;
    }

    private async ValueTask<Player> Connect(string? token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/draft"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers[GatewayOnlyMiddleware.HeaderName] = Secret;
                if (token is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        _connections.Add(connection);

        var player = new Player(connection);
        await connection.StartAsync();
        return player;
    }

    private static JsonElement Seat(JsonElement state, string seat) =>
        state.GetProperty("seats").EnumerateArray().Single(s => s.GetProperty("seat").GetString() == seat);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private sealed record Ticket(string Code, string Seat, string Token);

    internal sealed record HubResult(bool Ok, string? Error, string? Message);

    /// <summary>Bir oyuncunun bağlantısı: gelen LobbyUpdated olaylarını sırayla biriktirir.</summary>
    private sealed class Player
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

        private readonly HubConnection _connection;
        private readonly BlockingCollection<JsonElement> _updates = [];
        private int _received;

        public Player(HubConnection connection)
        {
            _connection = connection;
            connection.On<JsonElement>("LobbyUpdated", state =>
            {
                Interlocked.Increment(ref _received);
                _updates.Add(state);
            });
        }

        public int Received => Volatile.Read(ref _received);

        public Task Disconnect() => _connection.StopAsync();

        public Task<HubResult> Invoke(string method, string champion, Guid? actionId = null) =>
            _connection.InvokeAsync<HubResult>(method, champion, actionId ?? Guid.NewGuid());

        public Task<HubResult> Invoke(string method) =>
            _connection.InvokeAsync<HubResult>(method, Guid.NewGuid());

        /// <summary>Sıradaki olay (en fazla 5 sn beklenir).</summary>
        public Task<JsonElement> Next() => Task.Run(() =>
            _updates.TryTake(out var state, Wait) ? state : throw new TimeoutException("LobbyUpdated gelmedi."));

        /// <summary>Kısa bir süre içinde gelen olayların sonuncusu.</summary>
        public async Task<JsonElement> Last()
        {
            var state = await Next();
            await Task.Delay(100);
            while (_updates.TryTake(out var newer))
                state = newer;
            return state;
        }
    }
}
