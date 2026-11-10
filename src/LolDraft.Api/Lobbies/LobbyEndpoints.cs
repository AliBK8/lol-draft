using LolDraft.Application;
using LolDraft.Application.Lobbies;
using LolDraft.Domain;

namespace LolDraft.Api.Lobbies;

/// <summary>
/// Lobi uç noktaları (DESIGN.md §8.7). Bu katman sadece çevirir: JSON → değer nesnesi → Application,
/// dönen <see cref="Result"/> → HTTP kodu. Kural yok; kurallar Domain'de.
/// </summary>
internal static class LobbyEndpoints
{
    private const string BearerPrefix = "Bearer ";

    private static readonly Error InvalidFormat = new("invalid_format", "Seri formatı bo1, bo3 ya da bo5 olmalı.");
    private static readonly Error Unauthorized = new("unauthorized", "Geçerli bir token gerekli.");

    public static void MapLobbyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lobbies", Create);
        app.MapPost("/api/lobbies/{code}/join", Join);
        app.MapGet("/api/lobbies/{code}", Get);
    }

    private static async Task<IResult> Create(CreateLobbyRequest request, CreateLobby createLobby, CancellationToken ct)
    {
        if (ParseFormat(request.Format) is not { } format)
            return Failure(InvalidFormat);

        var ticket = await createLobby.Handle(format, request.Fearless, ct);
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

    private static async Task<IResult> Get(
        string code, HttpRequest request, IGuestTokenService tokens, GetLobby getLobby, CancellationToken ct)
    {
        // Gateway token'ı doğrulayıp geçirdi; API yine de kendisi doğrular ve kimliği imzalı claim'lerden okur.
        var identity = await ReadIdentity(request, tokens);
        if (identity is null)
            return Failure(Unauthorized);

        // Token başka bir lobi için verilmişse bu lobiyi göremez.
        if (identity.Lobby.Value != code)
            return Failure(LobbyErrors.NotInLobby);

        var view = await getLobby.Handle(identity, ct);
        return view.IsSuccess ? Results.Ok(LobbyResponse.Of(view.Value)) : Failure(view.Error!);
    }

    private static async Task<GuestIdentity?> ReadIdentity(HttpRequest request, IGuestTokenService tokens)
    {
        if (request.Headers.Authorization is not [{ } header]
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await tokens.Read(header[BearerPrefix.Length..]);
    }

    private static SeriesFormat? ParseFormat(string? value) => value switch
    {
        "bo1" => SeriesFormat.Bo1,
        "bo3" => SeriesFormat.Bo3,
        "bo5" => SeriesFormat.Bo5,
        _ => null,
    };

    private static string FormatName(SeriesFormat format) => $"bo{(int)format}";

    // İş hatası → HTTP kodu. Gövde her zaman aynı biçimde: { error, message }.
    private static IResult Failure(Error error)
    {
        var status = error.Code switch
        {
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "not_in_lobby" => StatusCodes.Status403Forbidden,
            "lobby_not_found" => StatusCodes.Status404NotFound,
            "lobby_full" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new ErrorResponse(error.Code, error.Message), statusCode: status);
    }

    internal sealed record CreateLobbyRequest(string? Format, bool Fearless);

    internal sealed record TicketResponse(string Code, string Seat, string Token)
    {
        public static TicketResponse Of(LobbyTicket ticket) =>
            new(ticket.Identity.Lobby.Value, ticket.Identity.Seat.ToString(), ticket.Token);
    }

    internal sealed record LobbyResponse(
        string Code, string Format, bool Fearless, string YourSeat, bool IsFull, int GameNumber, string BlueTeam, int WinsA, int WinsB)
    {
        public static LobbyResponse Of(LobbyView view) => new(
            view.Code.Value, FormatName(view.Format), view.Fearless, view.YourSeat.ToString(), view.IsFull,
            view.GameNumber, view.BlueTeam.ToString(), view.WinsA, view.WinsB);
    }

    internal sealed record ErrorResponse(string Error, string Message);
}
