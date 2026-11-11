using Yarp.ReverseProxy.Transforms;

namespace LolDraft.Gateway.Proxy;

/// <summary>
/// API'ye iletilen her isteğe uygulanan dönüşümler. API'deki GatewayOnlyMiddleware bu sırrı arar.
/// </summary>
internal static class GatewayTransforms
{
    // API'deki GatewayOnlyMiddleware.HeaderName ile aynı (iki proje birbirine bağımlı değil; sabit iki yerde).
    public const string GatewaySecretHeader = "X-Gateway-Secret";

    // API'deki GatewayOnlyMiddleware.ClientIpHeader ile aynı.
    public const string ClientIpHeader = "X-Client-Ip";

    public static IReverseProxyBuilder AddGatewayTransforms(this IReverseProxyBuilder proxy, GatewaySettings settings) =>
        proxy.AddTransforms(context =>
        {
            // İstemcinin gönderdiği sır başlığı silinir, yerine gerçek sır yazılır: kimse kendini "gateway" diye
            // tanıtamaz. (append: false zaten ezer; ayrı silme adımı, istemci değerinin hiçbir yolla geçmediğini açık eder.)
            context.RequestTransforms.Add(new RequestHeaderRemoveTransform(GatewaySecretHeader));
            context.AddRequestHeader(GatewaySecretHeader, settings.GatewaySecret, append: false);

            // İstemci IP'si (API'de IP başına lobi sınırı). Aynı desen: istemcinin yazdığı silinir, gateway'in gördüğü
            // IP yazılır. X-Forwarded-For'a güvenilmez: gateway onu güvenilen proxy yoksa işlemez ama silmez de,
            // istemcinin sahte değeri API'ye taşınabilirdi. RemoteIpAddress zaten ForwardedHeaders'tan geçti.
            // Dikkat: YARP istemci başlıklarını dönüşümlerden sonra tembel kopyalar; ProxyRequest.Headers'a doğrudan
            // yazmak istemcinin değerini silmez, yanına ekler. RequestTransform yardımcıları bu sırayı bilir.
            // Değer adresin kendisi değil istemci anahtarıdır (IPv6'da /64 öneki, bkz. ClientKey): API'deki
            // "IP başına lobi" sınırı gateway'in rate limit'leriyle aynı kovayı sayar.
            context.AddRequestTransform(transform =>
            {
                RequestTransform.RemoveHeader(transform, ClientIpHeader);
                RequestTransform.AddHeader(transform, ClientIpHeader, ClientKey.Of(transform.HttpContext));
                return ValueTask.CompletedTask;
            });
        });
}
