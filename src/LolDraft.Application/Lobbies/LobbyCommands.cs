using LolDraft.Application.Matches;
using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Oyuncunun lobideki hamleleri (hub'dan gelir): hazır, ban, pick, swap, kilitle.
/// Her hamle lobinin kilidi altında koşar ve başarılıysa yeni durumu aynı kilidin altında üretir;
/// böylece yayınlanan görüntü tam olarak bu hamleden sonraki durumdur (araya başka hamle giremez).
/// Hamle draft'ı bitirdiyse (ikinci kilit) maç da aynı kilidin altında oynanır (<see cref="PlayMatch"/>).
/// </summary>
public sealed class LobbyCommands(ILobbyRepository lobbies, IClock clock, PlayMatch playMatch, IMatchRepository matches)
{
    public Task<Result<LobbyUpdate>> Ready(GuestIdentity who, ActionId action, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Ready(who.Player, action, DraftTimings.Default, now), ct);

    public Task<Result<LobbyUpdate>> Ban(GuestIdentity who, ActionId action, ChampionId champion, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Ban(who.Player, action, champion, now), ct);

    public Task<Result<LobbyUpdate>> Pick(GuestIdentity who, ActionId action, ChampionId champion, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Pick(who.Player, action, champion, now), ct);

    public Task<Result<LobbyUpdate>> SwapRoles(GuestIdentity who, ActionId action, Role a, Role b, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.SwapRoles(who.Player, action, a, b, now), ct);

    public Task<Result<LobbyUpdate>> LockLineup(GuestIdentity who, ActionId action, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.LockLineup(who.Player, action, now), ct);

    /// <summary>Oyuncunun bir bağlantısı açıldı (hub). Yeni durum: koltuk "bağlı" görünür.</summary>
    public Task<Result<LobbyUpdate>> Connect(GuestIdentity who, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Connect(who.Player, now), ct);

    /// <summary>Oyuncunun bir bağlantısı kapandı. Son bağlantısıysa hükmen sayacı başlar.</summary>
    public Task<Result<LobbyUpdate>> Disconnect(GuestIdentity who, CancellationToken ct) =>
        Run(who, (lobby, now) => lobby.Disconnect(who.Player, now), ct);

    private async Task<Result<LobbyUpdate>> Run(GuestIdentity who, Func<Lobby, Instant, Result> act, CancellationToken ct)
    {
        var update = await lobbies.Execute(who.Lobby, lobby =>
        {
            // Token'daki koltuk lobideki gerçek koltukla aynı olmalı (GetLobby ile aynı kural).
            if (lobby.SeatOf(who.Player) != who.Seat)
                return Result<LobbyUpdate>.Fail(LobbyErrors.NotInLobby);

            // Saat kilidin içinde okunur: hamlelerin sırası ile zamanları çelişmez.
            var now = clock.Now;
            var result = act(lobby, now);
            if (result.IsFailure)
                return Result<LobbyUpdate>.Fail(result.Error!);

            // Görüntü maçtan sonra üretilir: skor ve oynanan maç listesi yayına girsin.
            var match = playMatch.PlayIfDrafted(lobby, now);
            return Result<LobbyUpdate>.Ok(new LobbyUpdate(LobbyView.Of(lobby, now), match));
        }, ct);

        // Kayıt kilidin dışında (depo async); yayından önce: link yayınlandığında maç okunabilir olsun.
        if (update is { IsSuccess: true, Value.Match: { } played })
            await matches.Save(played, ct);

        return update;
    }
}
