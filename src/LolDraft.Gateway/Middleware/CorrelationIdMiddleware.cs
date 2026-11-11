using System.Text.RegularExpressions;

namespace LolDraft.Gateway.Middleware;

/// <summary>
/// Her isteğe bir kimlik (DESIGN.md §8.6 "Sırlar ve kayıt"): gelen <see cref="HeaderName"/> geçerliyse o, değilse yenisi.
/// İstek başlığına yazılır (YARP API'ye iletir), cevaba da eklenir: istemci bir hatayı bildirirken bu kimliği verir.
/// </summary>
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public Task InvokeAsync(HttpContext context)
    {
        // İstemciden gelen kimliğe güvenilmez: log satırına girdiği için biçimi sıkı (harf, rakam, tire; en fazla 64).
        // Aksi halde "abc\nSAHTE LOG SATIRI" gibi bir değerle log zehirlenebilir (log injection).
        var incoming = context.Request.Headers[HeaderName];
        var id = incoming.Count == 1 && ValidId().IsMatch(incoming[0]!) ? incoming[0]! : Guid.NewGuid().ToString("N");

        context.Request.Headers[HeaderName] = id;
        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        return next(context);
    }

    // \z: dizinin tam sonu. $ sondaki \n'nin önünde de eşleşirdi: log zehirlemeye karşı kontrol bunu kaçırmamalı.
    [GeneratedRegex(@"^[A-Za-z0-9-]{1,64}\z")]
    private static partial Regex ValidId();
}
