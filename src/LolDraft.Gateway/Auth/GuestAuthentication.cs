using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace LolDraft.Gateway.Auth;

/// <summary>
/// Misafir JWT doğrulaması (DESIGN.md §8.4). Kayıt yok; lobi açan/katılan oyuncu API'den imzalı bir token alır
/// (sub = oyuncu, lobby = lobi kodu, seat = Mavi/Kırmızı, 2 saat). Gateway sadece kimliği doğrular: imza, süre,
/// yayıncı, hedef. Yetki (bu lobide mi, sıra onda mı) API ve domain'in işi; gateway lobinin durumunu bilmez.
/// </summary>
internal static class GuestAuthentication
{
    /// <summary>YARP rotalarının AuthorizationPolicy'si: geçerli bir misafir token'ı.</summary>
    public const string Policy = "guest";

    public const string LobbyClaim = "lobby";
    public const string SeatClaim = "seat";

    // SignalR WebSocket'te başlık gönderilemez; token sorgu parametresiyle gelir (§8.4 madde 7).
    private const string HubTokenQuery = "access_token";
    private const string HubPathPrefix = "/hubs";

    public static IServiceCollection AddGuestAuthentication(this IServiceCollection services, GatewaySettings settings)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claim adlarını olduğu gibi bırak ("sub" → uzun Microsoft URI'sine çevrilmesin).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = GatewaySettings.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = GatewaySettings.JwtAudience,
                    ValidateLifetime = true,
                    // Saat farkı toleransı (varsayılan 5 dk çok cömert; süresi dolan token 5 dk daha geçerdi).
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(settings.JwtSigningKey),
                    // Sadece HS256: "alg": "none" ya da başka algoritmayla imzalanmış token reddedilir
                    // (algoritma karıştırma saldırısı).
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                };

                options.Events = new JwtBearerEvents
                {
                    // Hub yolunda token sorgudan okunur; diğer yollarda sadece Authorization başlığından.
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.Request.Path.StartsWithSegments(HubPathPrefix)
                            && context.Request.Query[HubTokenQuery] is [{ Length: > 0 } token])
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        // "guest": imzası geçerli ve lobi + koltuk bilgisi taşıyan token. Hangi koltuk/sıra olduğu API'de denetlenir.
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(LobbyClaim)
                .RequireClaim(SeatClaim));

        return services;
    }
}
