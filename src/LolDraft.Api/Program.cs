// Oyun API'si (DESIGN.md §8.1): sadece iç ağda; dışarıdan her istek gateway'den geçer.
using LolDraft.Api;
using LolDraft.Api.Auth;
using LolDraft.Api.Background;
using LolDraft.Api.Hubs;
using LolDraft.Api.Lobbies;
using LolDraft.Api.Matches;
using LolDraft.Api.Middleware;
using LolDraft.Application.Lobbies;
using LolDraft.Application.Matches;
using LolDraft.Engine;
using LolDraft.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Sırlar başlangıçta doğrulanır; eksikse uygulama açılmaz.
var settings = ApiSettings.From(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(settings);

// Portların gerçekleri (lobi deposu, maç veritabanı, token, CSPRNG, şampiyon listesi) ve onları kullanan senaryolar.
builder.Services.AddInfrastructure(settings.JwtSigningKey, settings.MatchStorage);
builder.Services.AddSingleton(LobbyCapacity.Default);
builder.Services.AddSingleton<CreateLobby>();
builder.Services.AddSingleton<JoinLobby>();
builder.Services.AddSingleton<GetLobby>();
builder.Services.AddSingleton<LobbyCommands>();
builder.Services.AddSingleton<AdvanceLobbies>();

// Simülasyon (4f): motor durumsuz, tek örnek paylaşılır. Draft bitince maçı oynatan senaryo ve replay okuma.
builder.Services.AddSingleton(MatchEngine.CreateSimulator());
builder.Services.AddSingleton<PlayMatch>();
builder.Services.AddSingleton<GetMatch>();

// Replay saklama süresi (DESIGN.md §8.16): günde bir eski maçlar silinir.
builder.Services.AddSingleton(settings.MatchRetention);
builder.Services.AddSingleton<PruneMatches>();
builder.Services.AddHostedService<MatchRetentionService>();

// Lobi saati: draft adımlarının süresini sunucu tutar (istemcinin saati kullanılmaz).
builder.Services.AddHostedService<LobbyClockService>();
builder.Services.AddSingleton(TimeProvider.System);

// Misafir token'ı: doğrulamanın tek gerçeği GuestTokenService, handler token'ı istekten bulur.
builder.Services.AddAuthentication(GuestAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, GuestAuthenticationHandler>(GuestAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

// SignalR sınırları (DESIGN.md §8.6 DoS): mesaj en fazla 4 KB; 15 sn'de bir canlılık sinyali,
// 30 sn ses gelmeyen istemci kopmuş sayılır.
builder.Services.AddSignalR(hub =>
{
    hub.MaximumReceiveMessageSize = 4 * 1024;
    hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
    hub.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    // Mesaj limiti (§8.5): bağlantı başına saniyede 5, dakikada 60.
    hub.AddFilter<RateLimitHubFilter>();
});
builder.Services.AddSingleton<RateLimitHubFilter>();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // İstek gövdesi en fazla 16 KB (DESIGN.md §8.6 DoS sınırları); gateway de aynı sınırı uygular.
    kestrel.Limits.MaxRequestBodySize = 16 * 1024;
    // "Server: Kestrel" başlığı yazılım bilgisini sızdırır.
    kestrel.AddServerHeader = false;
});

var app = builder.Build();

// Maç veritabanı: bekleyen migration'lar uygulanmadan istek kabul edilmez (şema uyuşmazsa uygulama açılmaz).
await app.Services.InitializeAsync(CancellationToken.None);

// İlk iş: gateway'den gelmeyen istek hiçbir uç noktaya ulaşmaz.
app.UseMiddleware<GatewayOnlyMiddleware>();

// Gateway'in eklediği correlation id isteğin kimliği olur: iki uygulamanın logları aynı kimlikle birleştirilir.
// Değer gateway'de doğrulandı (biçim ve uzunluk); buraya sadece gateway'den gelen istek ulaşır.
app.Use((context, next) =>
{
    if (context.Request.Headers["X-Correlation-Id"] is [{ Length: > 0 } id])
        context.TraceIdentifier = id;
    return next(context);
});

app.UseAuthentication();
app.UseAuthorization();

// Sağlık kontrolü: gateway → API hattının çalıştığını gösterir.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapLobbyEndpoints();
app.MapMatchEndpoints();
app.MapHub<DraftHub>(DraftHub.Path);

await app.RunAsync();
