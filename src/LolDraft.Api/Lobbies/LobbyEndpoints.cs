using System.Security.Claims;
using LolDraft.Api.Auth;
using LolDraft.Api.Middleware;
using LolDraft.Application.Lobbies;
using LolDraft.Domain;

namespace LolDraft.Api.Lobbies;

/// <summary>
/// Lobi uç noktaları (DESIGN.md §8.7). Bu katman sadece çevirir: JSON → değer nesnesi → Application,
/// dönen <see cref="Result"/> → HTTP kodu. Kural yok; kurallar Domain'de.
/// </summary>
internal static class LobbyEndpoints
{
    private static readonly Error InvalidFormat = new("invalid_format", "Seri formatı bo1, bo3 ya da bo5 olmalı.");

    public static void MapLobbyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lobbies", Create);
        app.MapPost("/api/lobbies/{code}/join", Join);
        // Token yoksa ya da geçersizse 401 (GuestAuthenticationHandler); API gateway'e güvenir ama kendisi de doğrular.
        app.MapGet("/api/lobbies/{code}", Get)
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(GuestAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());
    }

    private static async Task<IResult> Create(
        CreateLobbyRequest request, HttpRequest http, CreateLobby createLobby, CancellationToken ct)
    {
        if (Names.ParseFormat(request.Format) is not { } format)
            return Failure(InvalidFormat);

        var ticket = await createLobby.Handle(format, request.Fearless, ClientKey(http), ct);
        return ticket.IsSuccess ? Results.Ok(TicketResponse.Of(ticket.Value)) : Failure(ticket.Error!);
    }

    private static async Task<IResult> Join(string code, JoinLobby joinLobby, CancellationToken ct)
    {
        // Biçimi bozuk kod var olamaz; "yok" ile aynı cevabı alır.
        var lobbyCode = LobbyCode.Create(code);
        if (lobbyCode.IsFailure)
            return Failure(LobbyErrors.NotFound);

        var ticket = await joinLobby.Handle(lobbyCode.Value, ct);
        return ticket.IsSuccess ? Results.Ok(TicketResponse.Of(ticket.Value)) : Failure(ticket.Error!);
    }

    private static async Task<IResult> Get(string code, ClaimsPrincipal user, GetLobby getLobby, CancellationToken ct)
    {
        var identity = GuestAuthenticationHandler.IdentityOf(user);

        // Token başka bir lobi için verilmişse bu lobiyi göremez.
        if (identity.Lobby.Value != code)
            return Failure(LobbyErrors.NotInLobby);

        var view = await getLobby.Handle(identity, ct);
        return view.IsSuccess ? Results.Ok(LobbyResponse.Of(view.Value)) : Failure(view.Error!);
    }

    // Açık lobi sınırının anahtarı: gateway'in yazdığı istemci IP'si. Başlık yoksa (gateway hep yazar; olmaması
    // yapılandırma hatası) hepsi tek kovaya düşer: sınır gevşemez, sıkılaşır.
    private static string ClientKey(HttpRequest http) =>
        http.Headers[GatewayOnlyMiddleware.ClientIpHeader] is [{ Length: > 0 } ip] ? ip : "unknown";

    // İş hatası → HTTP kodu; tabloda olmayan her iş hatası 400. Kodlar hata nesnelerinden okunur (düz metin yazım
    // hatası sessizce 400'e düşerdi).
    private static readonly Dictionary<string, int> StatusByCode = new()
    {
        [LobbyErrors.NotInLobby.Code] = StatusCodes.Status403Forbidden,
        [LobbyErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [LobbyErrors.Full.Code] = StatusCodes.Status409Conflict,
        [CreateLobby.TooManyLobbies.Code] = StatusCodes.Status429TooManyRequests,
        [CreateLobby.ServerBusy.Code] = StatusCodes.Status503ServiceUnavailable,
    };

    // Gövde her zaman aynı biçimde: { error, message }.
    private static IResult Failure(Error error) =>
        Results.Json(new ErrorResponse(error.Code, error.Message),
            statusCode: StatusByCode.GetValueOrDefault(error.Code, StatusCodes.Status400BadRequest));

    internal sealed record CreateLobbyRequest(string? Format, bool Fearless);

    internal sealed record TicketResponse(string Code, string Seat, string Token)
    {
        public static TicketResponse Of(LobbyTicket ticket) =>
            new(ticket.Identity.Lobby.Value, ticket.Identity.Seat.ToString(), ticket.Token);
    }

    /// <summary>İsteyenin koltuğu + hub'ın LobbyUpdated olayıyla aynı biçimde lobi durumu.</summary>
    internal sealed record LobbyResponse(string YourSeat, LobbyStateDto Lobby)
    {
        public static LobbyResponse Of(SeatedLobbyView view) => new(view.YourSeat.ToString(), LobbyStateDto.Of(view.Lobby));
    }

    internal sealed record ErrorResponse(string Error, string Message);
}
