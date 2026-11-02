namespace LolDraft.Gateway.Middleware;

/// <summary>
/// Güvenlik başlıkları (DESIGN.md §8.6 "Taşıma ve başlıklar"). Cevap gönderilmeden hemen önce (OnStarting) yazılır:
/// böylece API'den proxy'lenen cevaplara da girer ve API'nin gönderdiği bir başlık bunları ezemez.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // CSP: tarayıcı sadece kendi origin'imizden script/stil/bağlantı yükler (+ Google Fonts). Bir XSS açığı olsa bile
    // saldırganın dışarıdan script çekmesini ve veriyi başka bir sunucuya göndermesini engeller.
    // frame-ancestors 'none': sayfamız başka bir sitenin iframe'ine gömülemez (clickjacking).
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            // Tarayıcı içerik türünü tahmin etmesin: "resim" diye yüklenen bir dosya script gibi çalıştırılamaz.
            headers.XContentTypeOptions = "nosniff";
            // Başka siteye giderken sadece origin gider, yol ve sorgu (davet kodu, token) gitmez.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // frame-ancestors'u bilmeyen eski tarayıcılar için.
            headers.XFrameOptions = "DENY";
            // Kamera, mikrofon, konum gibi tarayıcı özelliklerine ihtiyacımız yok; kapalı.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // API'den gelen "Server" başlığı (yazılım bilgisi) dışarı çıkmaz.
            headers.Remove("Server");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
