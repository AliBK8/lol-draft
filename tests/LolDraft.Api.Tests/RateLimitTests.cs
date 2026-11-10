using System.Net.Http.Json;
using LolDraft.Api.Hubs;
using LolDraft.Api.Middleware;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace LolDraft.Api.Tests;

/// <summary>Hub mesaj limiti (DESIGN.md §8.5): sayacın kendisi (elle saat) ve hub üzerinden (gerçek saat).</summary>
public sealed class RateLimitTests : IAsyncDisposable
{
    private const string Secret = "test-gateway-secret-0123456789abcdef";
    private const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _factory = new WebApplicationFactory<GatewayOnlyMiddleware>()
        .WithWebHostBuilder(host => host.UseSetting("GATEWAY_SECRET", Secret).UseSetting("JWT_SIGNING_KEY", SigningKey));

    private HubConnection? _connection;

    // --- Kayan pencere ---

    [Fact]
    public void Pencere_dolunca_reddeder_eski_olay_cikinca_yine_kabul_eder()
    {
        var time = new ManualTime();
        var window = new SlidingWindow(5, TimeSpan.FromSeconds(1), time);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(window.TryAdd());
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.False(window.TryAdd());                    // 0,5 sn: pencerede 5 olay var

        time.Advance(TimeSpan.FromMilliseconds(550));     // 1,05 sn: 0,0'daki olay çıktı
        Assert.True(window.TryAdd());
        Assert.False(window.TryAdd());
    }

    [Fact]
    public void Reddedilen_deneme_pencereye_yazilmaz()
    {
        var time = new ManualTime();
        var window = new SlidingWindow(1, TimeSpan.FromSeconds(1), time);

        Assert.True(window.TryAdd());
        for (var i = 0; i < 10; i++)
            Assert.False(window.TryAdd());                // ısrar etmek pencereyi uzatmaz

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(window.TryAdd());
    }

    // --- Hub ---

    [Fact]
    public async Task Saniyede_altinci_mesaj_islenmez()
    {
        var connection = await ConnectToNewLobby();

        var results = new List<HubResult>();
        for (var i = 0; i < 6; i++)
            results.Add(await connection.InvokeAsync<HubResult>("Ready", Guid.NewGuid()));

        // İlk 5'i işlendi (rakip yok → lobby_not_full), 6.'sı hub metoduna hiç ulaşmadı.
        Assert.All(results.Take(5), r => Assert.Equal("lobby_not_full", r.Error));
        Assert.Equal("rate_limited", results[5].Error);
    }

    [Fact]
    public async Task Limiti_dakikada_10_kez_asan_baglanti_kapatilir()
    {
        var connection = await ConnectToNewLobby();
        var closed = new TaskCompletionSource();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        // 5 kabul + 10 ret: 10. retle bağlantı kapanır.
        for (var i = 0; i < 15 && connection.State == HubConnectionState.Connected; i++)
        {
            try
            {
                await connection.InvokeAsync<HubResult>("Ready", Guid.NewGuid());
            }
            catch (Exception)
            {
                break;   // kapanan bağlantıdaki çağrı hata verir
            }
        }

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private async Task<HubConnection> ConnectToNewLobby()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewayOnlyMiddleware.HeaderName, Secret);
        var ticket = (await (await client.PostAsJsonAsync("/api/lobbies", new { format = "bo1", fearless = false }))
            .Content.ReadFromJsonAsync<Ticket>())!;

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/draft"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers[GatewayOnlyMiddleware.HeaderName] = Secret;
                options.AccessTokenProvider = () => Task.FromResult<string?>(ticket.Token);
            })
            .Build();
        await _connection.StartAsync();
        return _connection;
    }

    private sealed record Ticket(string Code, string Seat, string Token);

    private sealed record HubResult(bool Ok, string? Error);

    /// <summary>Elle ilerletilen zaman (TimeProvider): sayaç testleri beklemez.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan span) => _timestamp += span.Ticks;
    }
}
