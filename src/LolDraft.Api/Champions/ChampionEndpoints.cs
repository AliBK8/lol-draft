using LolDraft.Application.Matches;

namespace LolDraft.Api.Champions;

/// <summary>
/// Şampiyon listesi (DESIGN.md §8.7, §8.17): GET /api/champions. Token istemez (herkese açık oyun verisi);
/// gateway'in genel IP limiti yeter. Liste sadece yeni sürümle değişir: tarayıcı 5 dk önbellekte tutar.
/// </summary>
internal static class ChampionEndpoints
{
    public static void MapChampionEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/champions", (HttpResponse response, ListChampions listChampions) =>
        {
            response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(new { champions = listChampions.Handle() });
        });
}
