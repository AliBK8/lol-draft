using Microsoft.AspNetCore.Http.Features;

namespace LolDraft.Gateway.Middleware;

/// <summary>
/// İstek gövdesi en fazla 16 KB (DESIGN.md §8.6 "DoS ve kaynak sınırları"). Oyunun hiçbir isteği bu kadar büyük
/// değil (en büyüğü bir draft hamlesi, birkaç yüz bayt); büyük gövde ancak sunucuyu yormak için gönderilir.
/// Content-Length bildirilmişse gövde okunmadan 413; bildirilmemişse (chunked) sunucu okurken aynı sınırı uygular.
/// </summary>
internal sealed class RequestSizeLimitMiddleware(RequestDelegate next)
{
    public const long MaxBodyBytes = 16 * 1024;

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = MaxBodyBytes;

        return next(context);
    }
}
