using LolDraft.Application;
using LolDraft.Application.Lobbies;
using LolDraft.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LolDraft.Infrastructure.Auth;

/// <summary>
/// Misafir JWT'si (DESIGN.md §8.4): sub = oyuncu, lobby = lobi kodu, seat = takım koltuğu (A/B), 2 saat, HS256.
/// Gateway aynı anahtarla imzayı doğrular (Gateway/Auth/GuestAuthentication.cs); yayıncı, hedef ve claim adları
/// iki tarafta aynı olmalı. API token'ı kendisi de doğrular: gateway'e güvenir ama claim'leri imzasız okumaz.
/// </summary>
internal sealed class GuestTokenService : IGuestTokenService
{
    private const string Issuer = "lol-draft";
    private const string Audience = "lol-draft-game";
    private const string LobbyClaim = "lobby";
    private const string SeatClaim = "seat";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _validation;
    private readonly IClock _clock;

    public GuestTokenService(byte[] signingKey, IClock clock)
    {
        _clock = clock;

        var key = new SymmetricSecurityKey(signingKey);
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            // Sadece HS256: "alg": "none" ya da başka algoritmayla gelen token reddedilir.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
        };
    }

    public string Issue(GuestIdentity identity)
    {
        var now = _clock.Now.Value.UtcDateTime;

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = identity.Player.Value,
                [LobbyClaim] = identity.Lobby.Value,
                [SeatClaim] = identity.Seat.ToString(),
            },
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            SigningCredentials = _credentials,
        });
    }

    public async Task<GuestIdentity?> Read(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, _validation);
        if (!result.IsValid)
            return null;

        // İmza geçerli olsa da claim'ler değer nesnelerinden geçer: biçimi bozuk kimlik hiç oluşmaz.
        var player = PlayerId.Create(Claim(result, JwtRegisteredClaimNames.Sub));
        var lobby = LobbyCode.Create(Claim(result, LobbyClaim));
        var seat = ParseSeat(Claim(result, SeatClaim));
        if (player.IsFailure || lobby.IsFailure || seat is null)
            return null;

        return new GuestIdentity(player.Value, lobby.Value, seat.Value);
    }

    private static string? Claim(TokenValidationResult result, string name) =>
        result.Claims.TryGetValue(name, out var value) ? value as string : null;

    // Enum.TryParse "0" ve "1"i de kabul ederdi; sadece tam adlar geçerli.
    private static TeamSlot? ParseSeat(string? value) => value switch
    {
        nameof(TeamSlot.A) => TeamSlot.A,
        nameof(TeamSlot.B) => TeamSlot.B,
        _ => null,
    };
}
