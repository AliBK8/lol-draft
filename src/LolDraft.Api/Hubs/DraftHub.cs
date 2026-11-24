using LolDraft.Api.Auth;
using LolDraft.Api.Lobbies;
using LolDraft.Api.Matches;
using LolDraft.Application.Lobbies;
using LolDraft.Application.Matches;
using LolDraft.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LolDraft.Api.Hubs;

/// <summary>
/// Canlı draft (DESIGN.md §8.7). İstemci sadece niyet gönderir (Ready, Ban, SkipBan, Pick, SwapRoles, LockLineup, ChoosePlan;
/// Preview hamle değil);
/// kurallar Domain'de, sonuç sunucunun. Her bağlantı lobisinin grubundadır; başarılı hamleden sonra
/// yeni durum gruptaki herkese <see cref="LobbyUpdated"/> ile gider, hamleyi yapan ayrıca cevap alır.
/// </summary>
[Authorize(AuthenticationSchemes = GuestAuthenticationHandler.SchemeName)]
internal sealed class DraftHub(LobbyCommands commands) : Hub
{
    public const string Path = "/hubs/draft";
    public const string LobbyUpdated = nameof(LobbyUpdated);
    public const string LobbyClosed = nameof(LobbyClosed);

    /// <summary>Draft bitti, maç oynandı: olay listesi gruba gider (sonra skoru taşıyan LobbyUpdated).</summary>
    public const string MatchReady = nameof(MatchReady);

    /// <summary>Rakibin ön izlemesi (§8.32): sadece gönderenin dışındakilere gider.</summary>
    public const string OpponentPreview = nameof(OpponentPreview);

    // Bağlantı lobiye "bağlandı" diye sayıldıysa işaret: kopunca sadece sayılan bağlantı düşülür.
    private static readonly object CountedKey = new();

    /// <summary>
    /// Bağlanınca: oyuncu gerçekten lobide mi? Öyleyse bağlantısı sayılır (hükmen sayacı durur), lobinin
    /// grubuna girer ve yeni durum herkese gider (rakip "bağlandı" görür).
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var who = Identity;
        var state = await commands.Connect(who, Context.ConnectionAborted);
        if (state.IsFailure)
        {
            // Token geçerli ama oyuncu o koltukta değil ya da lobi kapandı: bağlantı kapanır.
            Context.Abort();
            return;
        }

        Context.Items[CountedKey] = true;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(who.Lobby));
        await Clients.Group(Group(who.Lobby)).SendAsync(LobbyUpdated, LobbyStateDto.Of(state.Value.View));
    }

    /// <summary>Kopunca: bağlantı düşülür; oyuncunun son bağlantısıysa "yok" sayacı başlar, rakip görür.</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!Context.Items.ContainsKey(CountedKey))
            return;

        var who = Identity;
        // Bağlantı zaten kapandı: ConnectionAborted iptal edilmiş olur, onu kullanma.
        var state = await commands.Disconnect(who, CancellationToken.None);
        if (state.IsSuccess)
            await Clients.Group(Group(who.Lobby)).SendAsync(LobbyUpdated, LobbyStateDto.Of(state.Value.View));
    }

    public Task<HubResultDto> Ready(Guid actionId) =>
        Run(actionId, (who, action, ct) => commands.Ready(who, action, ct));

    public Task<HubResultDto> Ban(string champion, Guid actionId) =>
        WithChampion(champion, actionId, commands.Ban);

    public Task<HubResultDto> Pick(string champion, Guid actionId) =>
        WithChampion(champion, actionId, commands.Pick);

    /// <summary>Ban adımını bilerek boş geçer ("Ban yok", §8.33). Hamle: mesaj limitinde hamle kovasından.</summary>
    public Task<HubResultDto> SkipBan(Guid actionId) =>
        Run(actionId, (who, action, ct) => commands.SkipBan(who, action, ct));

    /// <summary>Swap fazında iki yuvayı değiştirir: "top", "jungle", "mid", "bot", "support".</summary>
    public Task<HubResultDto> SwapRoles(string a, string b, Guid actionId)
    {
        if (Names.ParseRole(a) is not { } roleA || Names.ParseRole(b) is not { } roleB)
            return Task.FromResult(HubResultDto.Failure(DraftErrors.InvalidRole));

        return Run(actionId, (who, action, ct) => commands.SwapRoles(who, action, roleA, roleB, ct));
    }

    public Task<HubResultDto> LockLineup(Guid actionId) =>
        Run(actionId, (who, action, ct) => commands.LockLineup(who, action, ct));

    /// <summary>
    /// Oyun planı (DESIGN.md §4.10): "balanced", "dragonLate", "earlyPressure", "botPressure", "split", "fastFinish",
    /// "invade". Gizli: rakibe sadece "seçti" gider (durumda bayrak); hangi plan olduğu maç sonunda replay'de.
    /// </summary>
    public Task<HubResultDto> ChoosePlan(string plan, Guid actionId)
    {
        if (PlanNames.ParsePlan(plan) is not { } parsed)
            return Task.FromResult(HubResultDto.Failure(DraftErrors.InvalidPlan));

        return Run(actionId, (who, action, ct) => commands.ChoosePlan(who, action, parsed, ct));
    }

    /// <summary>
    /// Ön izleme (§8.32): sırası gelen oyuncu havuzda bir şampiyona tıkladı (null: seçimi bıraktı). Hamle değil:
    /// actionId yok (kaybolan ön izleme tekrar denenmez), durum değişmez. Geçerliyse (sıra onda, şampiyon alınabilir)
    /// rakibe gider; gönderenin bu bağlantısına geri gelmez. Mesaj limiti ayrı kovadan (<see cref="RateLimitHubFilter"/>).
    /// </summary>
    public async Task<HubResultDto> Preview(string? champion)
    {
        ChampionId? id = null;
        if (champion is not null)
        {
            var parsed = ChampionId.Create(champion);
            if (parsed.IsFailure)
                return HubResultDto.Failure(parsed.Error!);
            id = parsed.Value;
        }

        var who = Identity;
        var preview = await commands.Preview(who, id, Context.ConnectionAborted);
        if (preview.IsFailure)
            return HubResultDto.Failure(preview.Error!);

        await Clients.OthersInGroup(Group(who.Lobby))
            .SendAsync(OpponentPreview, DraftPreviewDto.Of(preview.Value), Context.ConnectionAborted);
        return HubResultDto.Success;
    }

    public static string Group(LobbyCode lobby) => $"lobby:{lobby.Value}";

    private GuestIdentity Identity => GuestAuthenticationHandler.IdentityOf(Context.User!);

    private Task<HubResultDto> WithChampion(
        string champion, Guid actionId,
        Func<GuestIdentity, ActionId, ChampionId, CancellationToken, Task<Result<LobbyUpdate>>> command)
    {
        var id = ChampionId.Create(champion);
        if (id.IsFailure)
            return Task.FromResult(HubResultDto.Failure(id.Error!));

        return Run(actionId, (who, action, ct) => command(who, action, id.Value, ct));
    }

    // Ortak akış: kimlik → hamle → cevap; başarılıysa yeni durum lobideki herkese.
    private async Task<HubResultDto> Run(
        Guid actionId, Func<GuestIdentity, ActionId, CancellationToken, Task<Result<LobbyUpdate>>> command)
    {
        var action = ActionId.Create(actionId);
        if (action.IsFailure)
            return HubResultDto.Failure(action.Error!);

        var who = Identity;
        var result = await command(who, action.Value, Context.ConnectionAborted);
        if (result.IsFailure)
            return HubResultDto.Failure(result.Error!);

        await Publish(Clients.Group(Group(who.Lobby)), result.Value, Context.ConnectionAborted);
        return HubResultDto.Success;
    }

    /// <summary>
    /// Yeni durumu (ve hamle draft'ı bitirdiyse oynanan maçı) gruba yayınlar. Sıra: önce MatchReady, sonra skoru taşıyan
    /// LobbyUpdated; istemci oynatma moduna skoru görmeden girer (skor oynatma bitince açılır, karar 2026-11-12).
    /// Lobi saati de aynı sırayla yayınlar.
    /// </summary>
    public static async Task Publish(IClientProxy group, LobbyUpdate update, CancellationToken ct)
    {
        if (update.Match is { } match)
            await group.SendAsync(MatchReady, ReplayPayload.Of(match), ct);
        await group.SendAsync(LobbyUpdated, LobbyStateDto.Of(update.View), ct);
    }
}
