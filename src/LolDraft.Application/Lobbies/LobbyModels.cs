using LolDraft.Application.Matches;
using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Lobbies;

/// <summary>Token'ın taşıdığı kimlik: kim, hangi lobide, hangi koltukta.</summary>
public sealed record GuestIdentity(PlayerId Player, LobbyCode Lobby, TeamSlot Seat);

/// <summary>
/// Lobiye giren oyuncuya verilen bilet: kimliği, onu kanıtlayan token ve koltuk anahtarı
/// (sekme kapanınca koltuğu geri almak için, tek kullanımlık; <see cref="RejoinLobby"/>).
/// </summary>
public sealed record LobbyTicket(GuestIdentity Identity, string Token, string RejoinToken);

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
    IReadOnlyList<SeriesGame> Games,
    Instant ServerNow)
{
    /// <param name="now">Sunucu saati: istemci geri sayımı kendi saatiyle aradaki farkı düzelterek çizer.</param>
    /// <param name="plans">Plan fazında dizilimlerin plan uyumu (motordan).</param>
    public static LobbyView Of(Lobby lobby, Instant now, PlanAdvisor plans)
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
                .Select(seat => new SeatView(seat, lobby.IsConnected(seat), lobby.AbsentSince(seat), lobby.NameOf(seat)))],
            series.CurrentDraft is { } draft ? DraftView.Of(draft, series.Locks, plans) : null,
            [.. series.Games],
            now);
    }
}

/// <summary>
/// Draft'ın o anki durumu. Geri sayım için <see cref="Deadline"/> gider, saniyelik mesaj gitmez:
/// istemci kalan süreyi kendisi çizer, süre dolunca karar yine sunucunun (sayaç servisi + Timeout).
/// Oyun planı gizli: sadece takımın seçip seçmediği gider; uyum (herkesin görebileceği dizilimden) plan fazında.
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
    IReadOnlyList<ChampionId> FearlessLocks,
    bool BluePlanChosen,
    bool RedPlanChosen,
    IReadOnlyDictionary<MatchPlan, PlanFit>? BluePlanFits,
    IReadOnlyDictionary<MatchPlan, PlanFit>? RedPlanFits)
{
    public static DraftView Of(DraftSession draft, FearlessLocks locks, PlanAdvisor plans)
    {
        var planning = draft.Phase == DraftPhase.Plan;
        return new DraftView(
            draft.Phase,
            draft.Turn,
            draft.CurrentStep?.Kind,
            draft.Deadline,
            [.. draft.Actions],
            draft.Lineup(Side.Blue),
            draft.Lineup(Side.Red),
            draft.IsLineupLocked(Side.Blue),
            draft.IsLineupLocked(Side.Red),
            [.. locks.Champions.OrderBy(c => c.Value, StringComparer.Ordinal)],
            draft.HasChosenPlan(Side.Blue),
            draft.HasChosenPlan(Side.Red),
            planning ? plans.Fits(draft.Lineup(Side.Blue)!) : null,
            planning ? plans.Fits(draft.Lineup(Side.Red)!) : null);
    }
}

/// <summary>
/// Oturulmuş bir koltuk: oyuncu bağlı mı, değilse ne zamandan beri yok
/// (arayüz hükmen sayacını gösterir: yok_olduğu_an + 2 dk − serverNow), takım adı (verilmediyse null).
/// </summary>
public sealed record SeatView(TeamSlot Seat, bool Connected, Instant? AbsentSince, TeamName? Name);

/// <summary>Bir hamlenin sonucu: yeni durum ve hamle draft'ı bitirdiyse oynanan maç (yayınlanır: MatchReady).</summary>
public sealed record LobbyUpdate(LobbyView View, Matches.MatchRecord? Match);

/// <summary>Lobi görüntüsü + isteyen oyuncunun koltuğu (REST'te; hub'da koltuk bağlantıya bağlı).</summary>
public sealed record SeatedLobbyView(TeamSlot YourSeat, LobbyView Lobby);
