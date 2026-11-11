using LolDraft.Application.Lobbies;
using LolDraft.Domain;

namespace LolDraft.Api;

/// <summary>
/// İş hatası → HTTP cevabı, bütün uç noktalarda aynı (DESIGN.md §8.7). Gövde her zaman { error, message }.
/// Tabloda olmayan her iş hatası 400. Kodlar hata nesnelerinden okunur (düz metin yazım hatası sessizce 400'e düşerdi).
/// </summary>
internal static class ApiErrors
{
    private static readonly Dictionary<string, int> StatusByCode = new()
    {
        [LobbyErrors.NotInLobby.Code] = StatusCodes.Status403Forbidden,
        [LobbyErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [LobbyErrors.Full.Code] = StatusCodes.Status409Conflict,
        [CreateLobby.TooManyLobbies.Code] = StatusCodes.Status429TooManyRequests,
        [CreateLobby.ServerBusy.Code] = StatusCodes.Status503ServiceUnavailable,
        [MatchErrors.NotFound.Code] = StatusCodes.Status404NotFound,
    };

    public static IResult Failure(Error error) =>
        Results.Json(new ErrorResponse(error.Code, error.Message),
            statusCode: StatusByCode.GetValueOrDefault(error.Code, StatusCodes.Status400BadRequest));

    internal sealed record ErrorResponse(string Error, string Message);
}
