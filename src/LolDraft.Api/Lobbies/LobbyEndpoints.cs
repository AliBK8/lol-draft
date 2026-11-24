using System.Security.Claims;
using System.Text.Json;
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
    private static readonly Error InvalidBody = new("invalid_body", "İstek gövdesi okunamadı.");

    public static void MapLobbyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lobbies", Create);
        app.MapPost("/api/lobbies/{code}/join", Join);
        // Token istemez: token'ı kaybeden oyuncu içindir; kimliği koltuk anahtarı kanıtlar.
        app.MapPost("/api/lobbies/{code}/rejoin", Rejoin);
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
        var name = OptionalName(request.Name);
        if (name.IsFailure)
            return Failure(name.Error!);

        var ticket = await createLobby.Handle(format, request.Fearless, ClientKey(http), ct, name.Value);
        return ticket.IsSuccess ? Results.Ok(TicketResponse.Of(ticket.Value)) : Failure(ticket.Error!);
    }

    // Gövde isteğe bağlı: adsız katılma (eski istemci, geliştirme rakibi) gövdesiz ya da JSON olmayan başlıkla gelir.
    // Çerçevenin gövde bağlaması JSON olmayanı 415 ile reddederdi; o yüzden gövde sadece JSON'sa okunur.
    private static async Task<IResult> Join(string code, HttpRequest http, JoinLobby joinLobby, CancellationToken ct)
    {
        // Biçimi bozuk kod var olamaz; "yok" ile aynı cevabı alır.
        var lobbyCode = LobbyCode.Create(code);
        if (lobbyCode.IsFailure)
            return Failure(LobbyErrors.NotFound);

        JoinRequest? request = null;
        if (http.HasJsonContentType() && http.ContentLength is not 0)
        {
            try
            {
                request = await http.ReadFromJsonAsync<JoinRequest>(ct);
            }
            catch (JsonException)
            {
                return Failure(InvalidBody);
            }
        }

        var name = OptionalName(request?.Name);
        if (name.IsFailure)
            return Failure(name.Error!);

        var ticket = await joinLobby.Handle(lobbyCode.Value, ct, name.Value);
        return ticket.IsSuccess ? Results.Ok(TicketResponse.Of(ticket.Value)) : Failure(ticket.Error!);
    }

    // Takım adı isteğe bağlı: boş ya da sadece boşluk = ad yok (null); doluysa kurallara uymalı (TeamName).
    private static Result<TeamName?> OptionalName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<TeamName?>.Ok(null);

        var name = TeamName.Create(value);
        return name.IsSuccess ? Result<TeamName?>.Ok(name.Value) : Result<TeamName?>.Fail(name.Error!);
    }

    private static async Task<IResult> Rejoin(string code, RejoinRequest request, RejoinLobby rejoinLobby, CancellationToken ct)
    {
        var lobbyCode = LobbyCode.Create(code);
        if (lobbyCode.IsFailure)
            return Failure(LobbyErrors.NotFound);

        var ticket = await rejoinLobby.Handle(lobbyCode.Value, request.RejoinToken, ct);
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

    private static IResult Failure(Error error) => ApiErrors.Failure(error);

    internal sealed record CreateLobbyRequest(string? Format, bool Fearless, string? Name = null);

    internal sealed record JoinRequest(string? Name);

    internal sealed record RejoinRequest(string? RejoinToken);

    internal sealed record TicketResponse(string Code, string Seat, string Token, string RejoinToken)
    {
        public static TicketResponse Of(LobbyTicket ticket) =>
            new(ticket.Identity.Lobby.Value, ticket.Identity.Seat.ToString(), ticket.Token, ticket.RejoinToken);
    }

    /// <summary>İsteyenin koltuğu + hub'ın LobbyUpdated olayıyla aynı biçimde lobi durumu.</summary>
    internal sealed record LobbyResponse(string YourSeat, LobbyStateDto Lobby)
    {
        public static LobbyResponse Of(SeatedLobbyView view) => new(view.YourSeat.ToString(), LobbyStateDto.Of(view.Lobby));
    }
}
