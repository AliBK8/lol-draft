namespace LolDraft.Domain;

/// <summary>
/// Lobi (DESIGN.md §2, §8.4): iki takım koltuğu ve oynanacak seri.
/// Açan oyuncu A koltuğuna oturur, linkle gelen B'ye; boş koltuk bir kez alınır.
/// Koltuk renk değildir: renk her maç seriden gelir (<see cref="Series.SideOf"/>).
/// </summary>
public sealed class Lobby
{
    private readonly Dictionary<TeamSlot, PlayerId> _seats = [];

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
}
