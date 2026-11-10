using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Oyuncunun lobideki hamleleri (hub'dan gelir): hazır, ban, pick, swap, kilitle.
/// Her hamle lobinin kilidi altında koşar ve başarılıysa yeni durumu aynı kilidin altında üretir;
/// böylece yayınlanan görüntü tam olarak bu hamleden sonraki durumdur (araya başka hamle giremez).
/// </summary>
public sealed class LobbyCommands(ILobbyRepository lobbies, IClock clock)
{
    public Task<Result<LobbyView>> Ready(GuestIdentity who, ActionId action, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Ready(who.Player, action, DraftTimings.Default, now), ct);

    public Task<Result<LobbyView>> Ban(GuestIdentity who, ActionId action, ChampionId champion, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Ban(who.Player, action, champion, now), ct);

    public Task<Result<LobbyView>> Pick(GuestIdentity who, ActionId action, ChampionId champion, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Pick(who.Player, action, champion, now), ct);

    public Task<Result<LobbyView>> SwapRoles(GuestIdentity who, ActionId action, Role a, Role b, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.SwapRoles(who.Player, action, a, b, now), ct);

    public Task<Result<LobbyView>> LockLineup(GuestIdentity who, ActionId action, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.LockLineup(who.Player, action, now), ct);

    /// <summary>Oyuncunun bir bağlantısı açıldı (hub). Yeni durum: koltuk "bağlı" görünür.</summary>
    public Task<Result<LobbyView>> Connect(GuestIdentity who, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Connect(who.Player, now), ct);

    /// <summary>Oyuncunun bir bağlantısı kapandı. Son bağlantısıysa hükmen sayacı başlar.</summary>
    public Task<Result<LobbyView>> Disconnect(GuestIdentity who, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Disconnect(who.Player, now), ct);

    private Task<Result<LobbyView>> Run(GuestIdentity who, Func<Lobby, Instant, Result> act, CancellationToken ct) =>
        lobbies.Execute(who.Lobby, lobby =>
        {
            // Token'daki koltuk lobideki gerçek koltukla aynı olmalı (GetLobby ile aynı kural).
            if (lobby.SeatOf(who.Player) != who.Seat)
                return Result<LobbyView>.Fail(LobbyErrors.NotInLobby);

            // Saat kilidin içinde okunur: hamlelerin sırası ile zamanları çelişmez.
            var now = clock.Now;
            var result = act(lobby, now);
            return result.IsSuccess
                ? Result<LobbyView>.Ok(LobbyView.Of(lobby, now))
                : Result<LobbyView>.Fail(result.Error!);
        }, ct);
}
