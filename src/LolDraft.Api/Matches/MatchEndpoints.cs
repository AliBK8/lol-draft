using LolDraft.Application.Matches;
using LolDraft.Domain;

namespace LolDraft.Api.Matches;

/// <summary>
/// Tekrar izleme (DESIGN.md §8.7): GET /api/matches/{id}. Token istemez: link paylaşılır, kimlik 128 bit rastgele.
/// Gateway'de IP başına dakikada 60 (replay politikası; cevap büyük, ~50 KB).
/// </summary>
internal static class MatchEndpoints
{
    public static void MapMatchEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/matches/{id}", Get);

    private static async Task<IResult> Get(string id, GetMatch getMatch, CancellationToken ct)
    {
        // Biçimi bozuk kimlik var olamaz; "yok" ile aynı cevabı alır (lobi kodu gibi).
        var matchId = MatchId.Create(id);
        if (matchId.IsFailure)
            return ApiErrors.Failure(MatchErrors.NotFound);

        var match = await getMatch.Handle(matchId.Value, ct);
        return match.IsSuccess ? Results.Ok(MatchDto.Of(match.Value)) : ApiErrors.Failure(match.Error!);
    }
}
