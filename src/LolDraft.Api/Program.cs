// Oyun API'si (DESIGN.md §8.1): sadece iç ağda; dışarıdan her istek gateway'den geçer.
using LolDraft.Api;
using LolDraft.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Sırlar başlangıçta doğrulanır; eksikse uygulama açılmaz.
builder.Services.AddSingleton(ApiSettings.From(builder.Configuration));

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // İstek gövdesi en fazla 16 KB (DESIGN.md §8.6 DoS sınırları); gateway de aynı sınırı uygular.
    kestrel.Limits.MaxRequestBodySize = 16 * 1024;
    // "Server: Kestrel" başlığı yazılım bilgisini sızdırır.
    kestrel.AddServerHeader = false;
});

var app = builder.Build();

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

// Sağlık kontrolü: gateway → API hattının çalıştığını gösterir.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
