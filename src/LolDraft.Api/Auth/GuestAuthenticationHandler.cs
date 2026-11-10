using System.Security.Claims;
using System.Text.Encodings.Web;
using LolDraft.Application;
using LolDraft.Application.Lobbies;
using LolDraft.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LolDraft.Api.Auth;

/// <summary>
/// API'nin misafir token doğrulaması (DESIGN.md §8.4). Gateway imzayı zaten doğruladı; API yine de kendisi
/// doğrular (claim'leri imzasız okumaz) ve kimliği <see cref="GuestIdentity"/> olarak bağlantıya koyar.
/// Doğrulamanın tek gerçeği <see cref="IGuestTokenService"/>; bu sınıf sadece token'ı istekten bulur.
/// Token: Authorization: Bearer başlığından; hub yolunda başlık yoksa access_token sorgusundan
/// (WebSocket başlık gönderemez, gateway'deki kuralın aynısı).
/// </summary>
internal sealed class GuestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IGuestTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "guest";

    private const string BearerPrefix = "Bearer ";
    private const string HubTokenQuery = "access_token";
    private const string HubPathPrefix = "/hubs";
    private const string LobbyClaim = "lobby";
    private const string SeatClaim = "seat";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (FindToken() is not { } token)
            return AuthenticateResult.NoResult();

        if (await tokens.Read(token) is not { } identity)
            return AuthenticateResult.Fail("Geçersiz token.");

        var claims = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.Player.Value),
                new Claim(LobbyClaim, identity.Lobby.Value),
                new Claim(SeatClaim, identity.Seat.ToString()),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), SchemeName));
    }

    /// <summary>Doğrulanmış kullanıcının kimliği. Claim'leri bu handler yazdı; okunamıyorsa programcı hatası.</summary>
    public static GuestIdentity IdentityOf(ClaimsPrincipal user)
    {
        var player = PlayerId.Create(user.FindFirstValue(ClaimTypes.NameIdentifier));
        var lobby = LobbyCode.Create(user.FindFirstValue(LobbyClaim));
        var seat = Enum.Parse<TeamSlot>(user.FindFirstValue(SeatClaim)
                                        ?? throw new InvalidOperationException("Koltuk claim'i yok."));
        return new GuestIdentity(player.Value, lobby.Value, seat);
    }

    private string? FindToken()
    {
        if (Request.Headers.Authorization is [{ } header] && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return header[BearerPrefix.Length..];

        if (Request.Path.StartsWithSegments(HubPathPrefix) && Request.Query[HubTokenQuery] is [{ Length: > 0 } query])
            return query;

        return null;
    }
}
