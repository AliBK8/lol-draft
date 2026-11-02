using Yarp.ReverseProxy.Transforms;

namespace LolDraft.Gateway.Proxy;

/// <summary>
/// API'ye iletilen her isteğe uygulanan dönüşümler. API'deki GatewayOnlyMiddleware bu sırrı arar.
/// </summary>
internal static class GatewayTransforms
{
    // API'deki GatewayOnlyMiddleware.HeaderName ile aynı (iki proje birbirine bağımlı değil; sabit iki yerde).
    public const string GatewaySecretHeader = "X-Gateway-Secret";

    public static IReverseProxyBuilder AddGatewayTransforms(this IReverseProxyBuilder proxy, GatewaySettings settings) =>
        proxy.AddTransforms(context =>
        {
            // İstemcinin gönderdiği sır başlığı silinir, yerine gerçek sır yazılır: kimse kendini "gateway" diye
            // tanıtamaz. (append: false zaten ezer; ayrı silme adımı, istemci değerinin hiçbir yolla geçmediğini açık eder.)
            context.RequestTransforms.Add(new RequestHeaderRemoveTransform(GatewaySecretHeader));
            context.AddRequestHeader(GatewaySecretHeader, settings.GatewaySecret, append: false);
        });
}
