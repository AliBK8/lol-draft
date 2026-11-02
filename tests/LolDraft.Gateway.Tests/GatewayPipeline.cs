using System.Collections.Concurrent;
using System.Text;
using LolDraft.Api.Middleware;
using LolDraft.Gateway.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Forwarder;

namespace LolDraft.Gateway.Tests;

/// <summary>
/// Gateway ve API bellekte, uçtan uca: YARP'ın API'ye giden HTTP istemcisi gerçek ağ yerine API'nin test sunucusuna
/// bağlanır. Port açılmaz; istek gerçek hattan geçer (gateway middleware'leri → YARP → API middleware'leri).
/// API'nin önündeki gözcü (<see cref="ApiProbe"/>) API'ye hangi isteğin hangi sır başlığıyla ulaştığını kaydeder.
/// </summary>
internal sealed class GatewayPipeline : IDisposable
{
    public const string Secret = "test-gateway-secret-0123456789abcdef";
    public const string SigningKey = "test-jwt-signing-key-0123456789abcdefghij";

    private readonly WebApplicationFactory<GatewayOnlyMiddleware> _api;
    private readonly WebApplicationFactory<CorrelationIdMiddleware> _gateway;

    public GatewayPipeline(string signingKey = SigningKey)
    {
        _api = new WebApplicationFactory<GatewayOnlyMiddleware>().WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", Secret)
            .ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new ProbeFilter(Probe))));

        var apiHandler = _api.Server.CreateHandler();
        _gateway = new WebApplicationFactory<CorrelationIdMiddleware>().WithWebHostBuilder(host => host
            .UseSetting("GATEWAY_SECRET", Secret)
            .UseSetting("JWT_SIGNING_KEY", signingKey)
            .ConfigureTestServices(services =>
                services.AddSingleton<IForwarderHttpClientFactory>(new InMemoryForwarderClientFactory(apiHandler))));
    }

    public ApiProbe Probe { get; } = new();

    public HttpClient CreateClient() => _gateway.CreateClient();

    /// <summary>API'nin üreteceği misafir token'ının aynısı (§8.4): sub, lobby, seat; HS256.</summary>
    public static string Token(
        string key = SigningKey, TimeSpan? expiresIn = null, bool withLobby = true, string issuer = "lol-draft",
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromHours(2));
        var claims = new Dictionary<string, object> { ["sub"] = "player-1" };
        if (withLobby)
        {
            claims["lobby"] = "lobby-code";
            claims["seat"] = "Blue";
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "lol-draft-game",
            Claims = claims,
            IssuedAt = expires.AddHours(-3),
            NotBefore = expires.AddHours(-3),
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm),
        });
    }

    public void Dispose()
    {
        _gateway.Dispose();
        _api.Dispose();
    }

    /// <summary>API'ye ulaşan istekler: yol (sorgu dahil) ve gelen sır başlığının değerleri.</summary>
    internal sealed class ApiProbe
    {
        public ConcurrentQueue<(string Path, string[] Secrets)> Requests { get; } = new();

        public bool Reached(string path) => Requests.Any(r => r.Path == path);
    }

    // API'nin en önüne (GatewayOnlyMiddleware'den de önce) kayıt middleware'i ekler.
    private sealed class ProbeFilter(ApiProbe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                probe.Requests.Enqueue((context.Request.Path + context.Request.QueryString,
                    [.. context.Request.Headers[GatewayOnlyMiddleware.HeaderName].Select(v => v ?? "")]));
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    // YARP'ın HTTP istemcisi: gerçek ağ yerine API test sunucusunun handler'ı.
    private sealed class InMemoryForwarderClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }
}
