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

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        await _factory.DisposeAsync();
    }

    /// <summary>Lobi açar, ikinci oyuncu katılır, ikisi de hazır der; ev sahibinin gördüğü olayları döndürür.</summary>
    private async Task<ConcurrentQueue<JsonElement>> StartDraft()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        var host = (await (await client.PostAsJsonAsync("/api/lobbies", new { format = "bo1", fearless = false }))
            .Content.ReadFromJsonAsync<Ticket>())!;
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
