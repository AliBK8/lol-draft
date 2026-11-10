using LolDraft.Api.Auth;
using LolDraft.Api.Lobbies;
using LolDraft.Application.Lobbies;
using LolDraft.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LolDraft.Api.Hubs;

/// <summary>
/// Canlı draft (DESIGN.md §8.7). İstemci sadece niyet gönderir (Ready, Ban, Pick, SwapRoles, LockLineup);
/// kurallar Domain'de, sonuç sunucunun. Her bağlantı lobisinin grubundadır; başarılı hamleden sonra
/// yeni durum gruptaki herkese <see cref="LobbyUpdated"/> ile gider, hamleyi yapan ayrıca cevap alır.
/// </summary>
[Authorize(AuthenticationSchemes = GuestAuthenticationHandler.SchemeName)]
internal sealed class DraftHub(LobbyCommands commands, GetLobby getLobby) : Hub
{
    public const string Path = "/hubs/draft";
    public const string LobbyUpdated = nameof(LobbyUpdated);

    private static readonly Error InvalidRole = new("invalid_role", "Geçersiz yuva.");

    /// <summary>Bağlanınca: oyuncu gerçekten lobide mi? Öyleyse lobinin grubuna girer ve güncel durumu alır.</summary>
    public override async Task OnConnectedAsync()
    {
        var who = Identity;
        var state = await getLobby.Handle(who, Context.ConnectionAborted);
        if (state.IsFailure)
        {
            // Token geçerli ama oyuncu o koltukta değil: bağlantı kapanır.
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(who.Lobby));
        await Clients.Caller.SendAsync(LobbyUpdated, LobbyStateDto.Of(state.Value.Lobby));
    }

    public Task<HubResultDto> Ready(Guid actionId) =>
        Run(actionId, (who, action, ct) => commands.Ready(who, action, ct));

    public Task<HubResultDto> Ban(string champion, Guid actionId) =>
        WithChampion(champion, actionId, commands.Ban);

    public Task<HubResultDto> Pick(string champion, Guid actionId) =>
        WithChampion(champion, actionId, commands.Pick);

    /// <summary>Swap fazında iki yuvayı değiştirir: "top", "jungle", "mid", "bot", "support".</summary>
    public Task<HubResultDto> SwapRoles(string a, string b, Guid actionId)
    {
        if (Names.ParseRole(a) is not { } roleA || Names.ParseRole(b) is not { } roleB)
            return Task.FromResult(HubResultDto.Failure(InvalidRole));

        return Run(actionId, (who, action, ct) => commands.SwapRoles(who, action, roleA, roleB, ct));
    }

    public Task<HubResultDto> LockLineup(Guid actionId) =>
        Run(actionId, (who, action, ct) => commands.LockLineup(who, action, ct));

    public static string Group(LobbyCode lobby) => $"lobby:{lobby.Value}";

    private GuestIdentity Identity => GuestAuthenticationHandler.IdentityOf(Context.User!);

    private Task<HubResultDto> WithChampion(
        string champion, Guid actionId,
        Func<GuestIdentity, ActionId, ChampionId, CancellationToken, Task<Result<LobbyView>>> command)
    {
        var id = ChampionId.Create(champion);
        if (id.IsFailure)
            return Task.FromResult(HubResultDto.Failure(id.Error!));

        return Run(actionId, (who, action, ct) => command(who, action, id.Value, ct));
    }

    // Ortak akış: kimlik → hamle → cevap; başarılıysa yeni durum lobideki herkese.
    private async Task<HubResultDto> Run(
        Guid actionId, Func<GuestIdentity, ActionId, CancellationToken, Task<Result<LobbyView>>> command)
    {
        var action = ActionId.Create(actionId);
        if (action.IsFailure)
            return HubResultDto.Failure(action.Error!);

        var who = Identity;
        var result = await command(who, action.Value, Context.ConnectionAborted);
        if (result.IsFailure)
            return HubResultDto.Failure(result.Error!);

        await Clients.Group(Group(who.Lobby)).SendAsync(LobbyUpdated, LobbyStateDto.Of(result.Value));
        return HubResultDto.Success;
    }
}
