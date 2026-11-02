// Gateway (DESIGN.md §8.1): dışarıya açık tek giriş noktası. API ve hub'a sadece buradan ulaşılır.
// Bir isteğin yolu (sıra önemli):
//   iletilmiş başlıklar → correlation id → güvenlik başlıkları → gövde sınırı → HTTPS (production)
//   → yönlendirme (rota eşleşir mi?) → rate limit → kimlik (JWT) → yetki (rota politikası) → YARP → API
using LolDraft.Gateway;
using LolDraft.Gateway.Auth;
using LolDraft.Gateway.Middleware;
using LolDraft.Gateway.Proxy;
using LolDraft.Gateway.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Sırlar başlangıçta doğrulanır; eksikse uygulama açılmaz.
var settings = GatewaySettings.From(builder.Configuration);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = RequestSizeLimitMiddleware.MaxBodyBytes;
    kestrel.AddServerHeader = false;
});

// İstemci IP'si: sadece listedeki proxy'lerin X-Forwarded-For'una güvenilir. Liste boşsa hiçbirine (varsayılan
// "localhost'a güven" de kaldırılır): aksi halde herkes başlığa sahte IP yazıp rate limit'i atlatabilirdi.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in settings.TrustedProxies)
        options.KnownProxies.Add(proxy);
});

builder.Services.AddGatewayRateLimiting();
builder.Services.AddGuestAuthentication(settings);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddGatewayTransforms(settings);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestSizeLimitMiddleware>();

if (!app.Environment.IsDevelopment())
{
    // HSTS: tarayıcı bu siteye bir yıl boyunca sadece HTTPS ile gelir (ilk istekten sonra HTTP'ye düşürülemez).
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
// Rate limit kimlik doğrulamadan önce: sel saldırısı imza doğrulamasına (kriptografi) CPU harcatmadan durur.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Sadece appsettings.json'daki rotalar API'ye gider (beyaz liste). Eşleşmeyen yol gateway'de 404 olur, API'ye
// ulaşmaz: API'ye yeni bir uç nokta eklemek onu otomatik olarak dışarı açmaz.
app.MapReverseProxy();

app.Run();
