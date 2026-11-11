using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using LolDraft.Api.Middleware;
using LolDraft.Application;
using LolDraft.Domain;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LolDraft.Api.Tests;

/// <summary>
/// Sunucu sayacı (lobi saati) gerçek servisle: saat elle ilerletilir, 30 sn beklenmez.
/// Arka plan döngüsü gerçek (250 ms'de bir), sadece "şu an"ı test belirler.
/// </summary>
public sealed class LobbyClockTests : IAsyncDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    // Gerçek "şimdi"den başlar: token bu saatle imzalanır ama doğrulama sistem saatine bakar
    // (gateway de öyle), ileri bir tarih token'ı "henüz geçerli değil" yapardı.
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory;
    private readonly List<HubConnection> _connections = [];

    public LobbyClockTests()
    {
        _factory = new WebApplicationFactory<GatewayOnlyMiddleware>().WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", Secret)
            .UseSetting("JWT_SIGNING_KEY", SigningKey)
            .ConfigureTestServices(services => services.AddSingleton<IClock>(_clock)));
    }

    [Fact]
    public async Task Suresi_dolan_ban_adimi_bos_ban_olur_ve_yayinlanir()
    {
        var updates = await StartDraft();

        _clock.Advance(TimeSpan.FromSeconds(31));

        var state = await WaitFor(updates, s => Actions(s).GetArrayLength() == 1);
        var action = Actions(state)[0];
        Assert.True(action.GetProperty("byTimeout").GetBoolean());
        Assert.Equal(JsonValueKind.Null, action.GetProperty("champion").ValueKind);
        Assert.Equal("red", state.GetProperty("draft").GetProperty("turn").GetString());
    }

    [Fact]
    public async Task Sure_dolmadan_zaman_asimi_olmaz()
    {
        var updates = await StartDraft();

        _clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(600);   // en az iki tik

        Assert.DoesNotContain(updates, s => Actions(s).GetArrayLength() > 0);
    }

    [Fact]
    public async Task Pick_adiminda_sure_dolarsa_rastgele_uygun_sampiyon_secilir()
    {
        var updates = await StartDraft();

        // 6 ban adımı zaman aşımıyla geçer, 7. adım (mavi pick) de.
        for (var step = 1; step <= 7; step++)
        {
            _clock.Advance(TimeSpan.FromSeconds(31));
            var count = step;
            await WaitFor(updates, s => Actions(s).GetArrayLength() == count);
        }

        var pick = Actions(updates.Last())[6];
        Assert.Equal("pick", pick.GetProperty("kind").GetString());
        Assert.True(pick.GetProperty("byTimeout").GetBoolean());
        Assert.False(string.IsNullOrEmpty(pick.GetProperty("champion").GetString()));
    }

    // Kimse hamle yapmazsa bile maç oynanır: 20 adım ve swap zaman aşımıyla geçer, swap bitince maçı saat oynatır.
    [Fact]
    public async Task Swap_suresi_dolunca_maci_saat_oynatir_ve_yayinlar()
    {
        var updates = await StartDraft();
        var matches = new ConcurrentQueue<JsonElement>();
        _connections[0].On<JsonElement>("MatchReady", matches.Enqueue);

        for (var step = 1; step <= 20; step++)
        {
            _clock.Advance(TimeSpan.FromSeconds(31));
            var count = step;
            await WaitFor(updates, s => Actions(s).GetArrayLength() == count);
        }

        _clock.Advance(TimeSpan.FromSeconds(31));   // swap süresi
        var state = await WaitFor(updates, s => s.GetProperty("games").GetArrayLength() == 1);

        // bo1: tek maç seriyi bitirir.
        Assert.Equal("played", state.GetProperty("endReason").GetString());
        await WaitUntil(() => Task.FromResult(!matches.IsEmpty));
        Assert.True(matches.TryDequeue(out var match));
        Assert.Equal(state.GetProperty("games")[0].GetProperty("match").GetString(), match.GetProperty("id").GetString());
    }

    // --- Hükmen galibiyet ve kapanış ---

    [Fact]
    public async Task Kopan_oyuncu_2_dk_donmezse_rakip_hukmen_kazanir()
    {
        var updates = await StartDraft();
        await _connections[1].StopAsync();   // misafir (B) kopar
        await WaitFor(updates, s => !Seat(s, "B").GetProperty("connected").GetBoolean());

        _clock.Advance(TimeSpan.FromMinutes(2));

        var state = await WaitFor(updates, s => s.GetProperty("winner").GetString() is not null);
        Assert.Equal("A", state.GetProperty("winner").GetString());
        Assert.Equal("forfeit", state.GetProperty("endReason").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Iki_oyuncu_da_cikarsa_lobi_5_dk_sonra_kapanir()
    {
        var (client, host) = await CreateLobby();
        var connection = await Connect(host.Token, new());
        await connection.StopAsync();

        // Kopma sunucuda ayrıca işlenir (StopAsync bunu beklemez): "yok" görünene kadar bekle, sonra saati ilerlet.
        await WaitUntil(async () =>
        {
            var lobby = await GetLobby(client, host);
            return lobby.StatusCode == System.Net.HttpStatusCode.OK
                   && !Seat((await lobby.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lobby"), "A")
                       .GetProperty("connected").GetBoolean();
        });

        _clock.Advance(TimeSpan.FromMinutes(5));

        // Lobi silindi: token hâlâ geçerli ama lobi yok.
        await WaitUntil(async () => (await GetLobby(client, host)).StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hareketsiz_lobi_30_dk_sonra_kapanir_ve_bagli_olana_haber_verilir()
    {
        var (_, host) = await CreateLobby();
        var closed = new TaskCompletionSource();
        var connection = await Connect(host.Token, new());
        connection.On("LobbyClosed", () => closed.TrySetResult());

        _clock.Advance(TimeSpan.FromMinutes(30));

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        await _factory.DisposeAsync();
    }

    /// <summary>Lobi açar, ikinci oyuncu katılır, ikisi de hazır der; ev sahibinin gördüğü olayları döndürür.</summary>
    private async Task<(HttpClient Client, Ticket Host)> CreateLobby()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        var host = (await (await client.PostAsJsonAsync("/api/lobbies", new { format = "bo1", fearless = false }))
            .Content.ReadFromJsonAsync<Ticket>())!;
        return (client, host);
    }

    /// <summary>Bağlantılar: [0] ev sahibi (A), [1] misafir (B).</summary>
    private async Task<ConcurrentQueue<JsonElement>> StartDraft()
    {
        var (client, host) = await CreateLobby();
        var guest = (await (await client.PostAsync($"/api/lobbies/{host.Code}/join", null))
            .Content.ReadFromJsonAsync<Ticket>())!;

        var updates = new ConcurrentQueue<JsonElement>();
        var hostConnection = await Connect(host.Token, updates);
        var guestConnection = await Connect(guest.Token, new());

        await hostConnection.InvokeAsync<JsonElement>("Ready", Guid.NewGuid());
        await guestConnection.InvokeAsync<JsonElement>("Ready", Guid.NewGuid());
        await WaitFor(updates, s => s.GetProperty("draft").ValueKind == JsonValueKind.Object);
        return updates;
    }

    private async Task<HubConnection> Connect(string token, ConcurrentQueue<JsonElement> updates)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/draft"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers[GatewayOnlyMiddleware.HeaderName] = Secret;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        connection.On<JsonElement>("LobbyUpdated", updates.Enqueue);
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private static Task<HttpResponseMessage> GetLobby(HttpClient client, Ticket ticket)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/lobbies/{ticket.Code}");
        request.Headers.Authorization = new("Bearer", ticket.Token);
        return client.SendAsync(request);
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Beklenen durum 5 sn içinde oluşmadı.");
            await Task.Delay(20);
        }
    }

    private static JsonElement Seat(JsonElement state, string seat) =>
        state.GetProperty("seats").EnumerateArray().Single(s => s.GetProperty("seat").GetString() == seat);

    private static JsonElement Actions(JsonElement state) =>
        state.GetProperty("draft") is { ValueKind: JsonValueKind.Object } draft
            ? draft.GetProperty("actions")
            : JsonDocument.Parse("[]").RootElement;

    private static async Task<JsonElement> WaitFor(ConcurrentQueue<JsonElement> updates, Func<JsonElement, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var match = updates.LastOrDefault(condition);
            if (match.ValueKind != JsonValueKind.Undefined)
                return match;
            Assert.True(DateTime.UtcNow < deadline, "Beklenen LobbyUpdated 5 sn içinde gelmedi.");
            await Task.Delay(20);
        }
    }

    private sealed record Ticket(string Code, string Seat, string Token);

    /// <summary>Elle ilerletilen saat: test "31 sn geçti" der, beklemez.</summary>
    private sealed class ManualClock(DateTimeOffset start) : IClock
    {
        private long _ticks = start.UtcTicks;

        public Instant Now => new(new DateTimeOffset(Interlocked.Read(ref _ticks), TimeSpan.Zero));

        public void Advance(TimeSpan span) => Interlocked.Add(ref _ticks, span.Ticks);
    }
}
