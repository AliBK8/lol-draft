using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Token'ın taşıdığı kimlik: kim, hangi lobide, hangi koltukta.</summary>
public sealed record GuestIdentity(PlayerId Player, LobbyCode Lobby, TeamSlot Seat);

/// <summary>Lobiye giren oyuncuya verilen bilet: kimliği ve onu kanıtlayan token.</summary>
public sealed record LobbyTicket(GuestIdentity Identity, string Token);

/// <summary>
/// Lobinin o anki görüntüsü: iki oyuncuya da aynısı gider (kimin hangi koltukta olduğu token'da).
/// Lobinin kilidi altında üretilen kopya; canlı lobi dışarı çıkmaz.
/// </summary>
/// <param name="Revision">Durumun sürümü (<see cref="Lobby.Revision"/>): istemci eski sürümü atar.</param>
public sealed record LobbyView(
    LobbyCode Code,
    long Revision,
    SeriesFormat Format,
    bool Fearless,
    bool IsFull,
    int GameNumber,
    TeamSlot BlueTeam,
    int WinsA,
    int WinsB,
    TeamSlot? Winner,
    SeriesEndReason? EndReason,
    IReadOnlyList<TeamSlot> Ready,
    IReadOnlyList<SeatView> Seats,
    DraftView? Draft,
    Instant ServerNow)
{
    /// <param name="now">Sunucu saati: istemci geri sayımı kendi saatiyle aradaki farkı düzelterek çizer.</param>
    public static LobbyView Of(Lobby lobby, Instant now)
    {
        var series = lobby.Series;
        return new LobbyView(
            lobby.Code,
            lobby.Revision,
            series.Format,
            series.Fearless,
            lobby.IsFull,
            series.GameNumber,
            series.BlueTeam,
            series.Wins(TeamSlot.A),
            series.Wins(TeamSlot.B),
            series.Winner,
            series.EndReason,
            [.. lobby.ReadySeats.Order()],
            [.. Enum.GetValues<TeamSlot>()
                .Where(seat => lobby.PlayerAt(seat) is not null)
                .Select(seat => new SeatView(seat, lobby.IsConnected(seat), lobby.AbsentSince(seat)))],
            series.CurrentDraft is { } draft ? DraftView.Of(draft, series.Locks) : null,
            now);
    }
}

/// <summary>
/// Draft'ın o anki durumu. Geri sayım için <see cref="Deadline"/> gider, saniyelik mesaj gitmez:
/// istemci kalan süreyi kendisi çizer, süre dolunca karar yine sunucunun (sayaç servisi + Timeout).
/// </summary>
public sealed record DraftView(
    DraftPhase Phase,
    Side? Turn,
    DraftActionKind? StepKind,
    Instant? Deadline,
    IReadOnlyList<DraftAction> Actions,
    TeamLineup? BlueLineup,
    TeamLineup? RedLineup,
    bool BlueLocked,
    bool RedLocked,
    IReadOnlyList<ChampionId> FearlessLocks)
{
    public static DraftView Of(DraftSession draft, FearlessLocks locks) => new(
        draft.Phase,
        draft.Turn,
        draft.CurrentStep?.Kind,
        draft.Deadline,
        [.. draft.Actions],
        draft.Lineup(Side.Blue),
        draft.Lineup(Side.Red),
        draft.IsLineupLocked(Side.Blue),
        draft.IsLineupLocked(Side.Red),
        [.. locks.Champions.OrderBy(c => c.Value, StringComparer.Ordinal)]);
}

/// <summary>
/// Oturulmuş bir koltuk: oyuncu bağlı mı, değilse ne zamandan beri yok
/// (arayüz hükmen sayacını gösterir: yok_olduğu_an + 2 dk − serverNow).
/// </summary>
public sealed record SeatView(TeamSlot Seat, bool Connected, Instant? AbsentSince);

/// <summary>Lobi görüntüsü + isteyen oyuncunun koltuğu (REST'te; hub'da koltuk bağlantıya bağlı).</summary>
public sealed record SeatedLobbyView(TeamSlot YourSeat, LobbyView Lobby);
