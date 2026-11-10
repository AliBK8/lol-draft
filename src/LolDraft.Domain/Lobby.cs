namespace LolDraft.Domain;

/// <summary>
/// Lobi (DESIGN.md §2, §8.4): iki takım koltuğu ve oynanacak seri.
/// Açan oyuncu A koltuğuna oturur, linkle gelen B'ye; boş koltuk bir kez alınır.
/// Koltuk renk değildir: renk her maç seriden gelir (<see cref="Series.SideOf"/>).
/// Oyuncunun hamleleri buradan geçer: yetki (lobide mi), koltuk → renk çevirisi ve tekrar koruması burada,
/// draft kuralları <see cref="DraftSession"/>'da.
/// </summary>
public sealed class Lobby
{
    // Son 64 hamle kimliği hatırlanır: çift tıklama milisaniyeler içinde gelir, bir draft ~30 hamle.
    private const int RecentActionCapacity = 64;

    private readonly Dictionary<TeamSlot, PlayerId> _seats = [];
    private readonly HashSet<TeamSlot> _ready = [];
    private readonly Dictionary<(PlayerId, ActionId), Result> _recentActions = [];
    private readonly Queue<(PlayerId, ActionId)> _recentOrder = new();

    private Lobby(LobbyCode code, Series series, PlayerId host)
    {
        Code = code;
        Series = series;
        _seats[TeamSlot.A] = host;
    }

    /// <summary>
    /// Seri ayarları lobi açılırken doğrulanır: havuza sığmayan kombinasyon (ör. 40'lık havuzda
    /// fearless bo5) daha lobi oluşmadan reddedilir, rakip davet edildikten sonra değil.
    /// </summary>
    public static Result<Lobby> Create(
        LobbyCode code, PlayerId host, SeriesFormat format, bool fearless, IEnumerable<ChampionId> pool, TeamSlot firstBlue)
    {
        var series = Series.Start(format, fearless, pool, firstBlue);
        return series.IsSuccess
            ? Result<Lobby>.Ok(new Lobby(code, series.Value, host))
            : Result<Lobby>.Fail(series.Error!);
    }

    public LobbyCode Code { get; }

    public Series Series { get; }

    public bool IsFull => _seats.Count == Enum.GetValues<TeamSlot>().Length;

    public PlayerId? PlayerAt(TeamSlot seat) => _seats.TryGetValue(seat, out var player) ? player : null;

    /// <summary>Sıradaki draft için hazır olan koltuklar.</summary>
    public IReadOnlySet<TeamSlot> ReadySeats => _ready;

    /// <summary>Oyuncunun koltuğu; lobide değilse null. Yetki kontrolünün dayanağı.</summary>
    public TeamSlot? SeatOf(PlayerId player)
    {
        foreach (var (seat, seated) in _seats)
        {
            if (seated == player)
                return seat;
        }

        return null;
    }

    /// <summary>Oyuncuyu ilk boş koltuğa oturtur.</summary>
    public Result<TeamSlot> Join(PlayerId player)
    {
        if (SeatOf(player) is not null)
            return Result<TeamSlot>.Fail(LobbyErrors.AlreadyInLobby);

        foreach (var seat in Enum.GetValues<TeamSlot>())
        {
            if (_seats.TryAdd(seat, player))
                return Result<TeamSlot>.Ok(seat);
        }

        return Result<TeamSlot>.Fail(LobbyErrors.Full);
    }

    /// <summary>
    /// Oyuncu sıradaki maç için hazır. İki koltuk da hazır olunca draft başlar ve hazırlar sıfırlanır
    /// (serinin sonraki maçında yeniden "hazır" denir).
    /// </summary>
    public Result Ready(PlayerId player, ActionId action, DraftTimings timings, Instant now) =>
        Once(player, action, seat =>
        {
            if (!IsFull)
                return Result.Fail(LobbyErrors.NotFull);
            if (Series.IsFinished)
                return Result.Fail(SeriesErrors.SeriesFinished);
            if (Series.CurrentDraft is not null)
                return Result.Fail(SeriesErrors.GameInProgress);

            _ready.Add(seat);
            if (_ready.Count < _seats.Count)
                return Result.Ok();

            var draft = Series.StartDraft(timings, now);
            if (draft.IsFailure)
                return Result.Fail(draft.Error!);

            _ready.Clear();
            return Result.Ok();
        });

    public Result Ban(PlayerId player, ActionId action, ChampionId champion, Instant now) =>
        OnDraft(player, action, (draft, side) => draft.Ban(side, champion, now));

    public Result Pick(PlayerId player, ActionId action, ChampionId champion, Instant now) =>
        OnDraft(player, action, (draft, side) => draft.Pick(side, champion, now));

    public Result SwapRoles(PlayerId player, ActionId action, Role a, Role b, Instant now) =>
        OnDraft(player, action, (draft, side) => draft.SwapRoles(side, a, b, now));

    public Result LockLineup(PlayerId player, ActionId action, Instant now) =>
        OnDraft(player, action, (draft, side) => draft.LockLineup(side, now));

    // Koltuk → bu maçtaki renk. Token renk taşımaz; renk her maç seriden okunur.
    private Result OnDraft(PlayerId player, ActionId action, Func<DraftSession, Side, Result> act) =>
        Once(player, action, seat => Series.CurrentDraft is { } draft
            ? act(draft, Series.SideOf(seat))
            : Result.Fail(DraftErrors.PhaseClosed));

    /// <summary>
    /// Hamleyi bir kez uygular. Aynı oyuncudan aynı kimlikle ikinci istek gelirse hamle tekrarlanmaz,
    /// ilk sonuç (başarılı ya da hatalı) aynen döner.
    /// </summary>
    private Result Once(PlayerId player, ActionId action, Func<TeamSlot, Result> act)
    {
        // Lobide olmayanın hamlesi hatırlanmaz: başkasının kimlik listesini dolduramasın.
        if (SeatOf(player) is not { } seat)
            return Result.Fail(LobbyErrors.NotInLobby);

        var key = (player, action);
        if (_recentActions.TryGetValue(key, out var previous))
            return previous;

        var result = act(seat);

        _recentActions[key] = result;
        _recentOrder.Enqueue(key);
        if (_recentOrder.Count > RecentActionCapacity)
            _recentActions.Remove(_recentOrder.Dequeue());

        return result;
    }
}
