namespace LolDraft.Gateway;

/// <summary>
/// React istemcisinin build'i (DESIGN.md §8.1, §8.17): gateway `wwwroot`'tan statik dosya olarak sunar; tarayıcı tek
/// origin görür (CORS yok, CSP 'self' yeter). Docker'da `web/dist` buraya kopyalanır; geliştirmede Vite kendi sunar.
/// </summary>
internal static class WebClient
{
    private const string IndexPath = "/index.html";

    /// <summary>
    /// Yönlendirmeden sonra çağrılır: bir rota (YARP) eşleştiyse ya da metot yanlışsa (405) dokunmaz.
    /// Eşleşmeyen istemci yolu (/l/{kod}, /m/{id}) index.html'e çevrilir, yolu React çözer (SPA).
    /// Catch-all fallback rotası kullanılmaz: aday rota olarak 405 tespitini bozuyordu (GET /api/lobbies 404 oluyordu).
    /// </summary>
    public static void UseWebClient(this WebApplication app)
    {
        app.Use((context, next) =>
        {
            if (context.GetEndpoint() is null && IsClientRoute(context.Request))
                context.Request.Path = IndexPath;
            return next(context);
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                // index.html hep tazelenir: yeni sürümde yeni hash'li dosyalara işaret eder. Vite build'inde /assets
                // altındaki adlar içerik hash'i taşır: değişen dosya yeni ad alır, eskisi sonsuza kadar önbellekte
                // kalabilir. Diğerleri (şampiyon resimleri vb.) bir gün.
                context.Context.Response.Headers.CacheControl =
                    path == IndexPath ? "no-cache"
                    : path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable"
                    : "public, max-age=86400";
            },
        });
    }

    // İstemci yolu: GET/HEAD, uzantısız (olmayan app.js 404 kalır, HTML'i script diye yüklemez), /api ve /hubs dışı
    // (beyaz listede olmayan API yolu index.html ile "başarılı" görünmemeli; büyük/küçük harf fark etmez).
    private static bool IsClientRoute(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
        && !Path.HasExtension(request.Path.Value);
}
