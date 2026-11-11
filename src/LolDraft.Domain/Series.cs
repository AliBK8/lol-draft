namespace LolDraft.Domain;

/// <summary>
/// Bir seri (bo1/bo3/bo5, DESIGN.md §2): skoru takıma yazar, her maçın renklerini ve
/// fearless kilitlerini belirler, maçın draft'ını başlatır.
/// Akış: <see cref="StartDraft"/> → draft oynanır → simülasyon → <see cref="RecordGame"/> → tekrar.
/// Saf: zar (ilk mavi kim) ve saat dışarıdan gelir.
/// Değiştiren metotlar internal: dışarıdan tek kapı <see cref="Lobby"/> (aggregate kökü). Böylece koltuk, sıra ve
/// tekrar kontrolü atlanamaz; dışarısı seriyi sadece okur.
/// </summary>
public sealed class Series
{
    private readonly HashSet<ChampionId> _pool;
    private readonly TeamSlot _firstBlue;
    private readonly List<SeriesGame> _games = [];
    private readonly Dictionary<TeamSlot, int> _wins = new() { [TeamSlot.A] = 0, [TeamSlot.B] = 0 };

    private Series(SeriesFormat format, bool fearless, HashSet<ChampionId> pool, TeamSlot firstBlue)
    {
        Format = format;
        Fearless = fearless;
        _pool = pool;
        _firstBlue = firstBlue;
    }

    /// <param name="firstBlue">İlk maçta mavi olan takım; sunucu rastgele belirler.</param>
    internal static Result<Series> Start(SeriesFormat format, bool fearless, IEnumerable<ChampionId> pool, TeamSlot firstBlue)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Bilinmeyen seri formatı.");
        if (!Enum.IsDefined(firstBlue))
            throw new ArgumentOutOfRangeException(nameof(firstBlue), firstBlue, "Bilinmeyen takım.");

        var poolSet = pool.ToHashSet();

        // Havuz serinin en uzun hâline yetmeli; yoksa seri ortasında draft başlatılamaz.
        // Fearless'ta her maç 10 pick kilitlenir: son maçtan önce (maç sayısı − 1) × 10 kilit olur
        // ve geriye en az 20 şampiyon (10 ban + 10 pick) kalmalı.
        // Örnek, 40'lık havuz: bo3 → 40 − 20 = 20 (sığar), bo5 → 40 − 40 = 0 (sığmaz).
        var picksPerGame = DraftOrder.Steps.Count(s => s.Kind == DraftActionKind.Pick);
        var lockedBeforeLastGame = fearless ? ((int)format - 1) * picksPerGame : 0;
        if (poolSet.Count - lockedBeforeLastGame < DraftSession.MinimumAvailable)
            return Result<Series>.Fail(DraftErrors.PoolTooSmall);

        return Result<Series>.Ok(new Series(format, fearless, poolSet, firstBlue));
    }

    public SeriesFormat Format { get; }

    public bool Fearless { get; }

    public IReadOnlyList<SeriesGame> Games => _games;

    /// <summary>Sıradaki (ya da oynanmakta olan) maçın numarası, 1'den başlar.</summary>
    public int GameNumber => _games.Count + 1;

    /// <summary>Seriyi almak için gereken galibiyet: bo1 → 1, bo3 → 2, bo5 → 3.</summary>
    public int WinsNeeded => (int)Format / 2 + 1;

    /// <summary>Seriyi kazanan takım; seri sürüyorsa null. Hükmen bitmişse kaçanın rakibi.</summary>
    public TeamSlot? Winner =>
        ForfeitedBy is { } loser ? loser.Opponent() :
        _wins[TeamSlot.A] >= WinsNeeded ? TeamSlot.A :
        _wins[TeamSlot.B] >= WinsNeeded ? TeamSlot.B : null;

    public bool IsFinished => Winner is not null;

    /// <summary>Seri nasıl bitti; sürüyorsa null.</summary>
    public SeriesEndReason? EndReason =>
        ForfeitedBy is not null ? SeriesEndReason.Forfeit :
        IsFinished ? SeriesEndReason.Played : null;

    /// <summary>Hükmen kaybeden takım (bağlantısı kopup dönmeyen); yoksa null.</summary>
    public TeamSlot? ForfeitedBy { get; private set; }

    /// <summary>İlk draft başladı mı? Hükmen galibiyet ancak seri başladıktan sonra işler.</summary>
    public bool HasStarted => _games.Count > 0 || CurrentDraft is not null;

    /// <summary>Oynanmakta olan maçın draft'ı; maç yokken null.</summary>
    public DraftSession? CurrentDraft { get; private set; }

    /// <summary>Sıradaki maçta mavi olan takım: renkler her maç değişir (1., 3., 5. maçta ilk mavi yine mavi).</summary>
    public TeamSlot BlueTeam => _games.Count % 2 == 0 ? _firstBlue : _firstBlue.Opponent();

    public Side SideOf(TeamSlot team) => team == BlueTeam ? Side.Blue : Side.Red;

    public TeamSlot TeamOn(Side side) => side == Side.Blue ? BlueTeam : BlueTeam.Opponent();

    public int Wins(TeamSlot team) => _wins[team];

    /// <summary>
    /// Sıradaki maçın fearless kilitleri: önceki maçların bütün pick'leri, iki takım için de.
    /// Ban'lar taşınmaz. Fearless kapalıysa boş.
    /// </summary>
    public FearlessLocks Locks => Fearless
        ? new FearlessLocks(_games.SelectMany(g => g.Draft.Blue.Champions.Concat(g.Draft.Red.Champions)))
        : FearlessLocks.None;

    /// <summary>Sıradaki maçın draft'ını o anki kilitlerle başlatır.</summary>
    internal Result<DraftSession> StartDraft(DraftTimings timings, Instant now)
    {
        if (IsFinished)
            return Result<DraftSession>.Fail(SeriesErrors.SeriesFinished);
        if (CurrentDraft is not null)
            return Result<DraftSession>.Fail(SeriesErrors.GameInProgress);

        var draft = DraftSession.Start(_pool, Locks, timings, now);
        if (draft.IsSuccess)
            CurrentDraft = draft.Value;
        return draft;
    }

    /// <summary>
    /// Seri hükmen biter: <paramref name="loser"/> kaybeder, rakibi seriyi alır. Yarım kalan draft atılır
    /// (oynanmamış maç skora yazılmaz). Ne zaman uygulanacağına lobi karar verir (bağlantı takibi).
    /// </summary>
    internal Result Forfeit(TeamSlot loser)
    {
        if (!Enum.IsDefined(loser))
            throw new ArgumentOutOfRangeException(nameof(loser), loser, "Bilinmeyen takım.");
        if (IsFinished)
            return Result.Fail(SeriesErrors.SeriesFinished);

        ForfeitedBy = loser;
        CurrentDraft = null;
        return Result.Ok();
    }

    /// <summary>
    /// Simülasyon bitince maçın kazananını yazar. Motor rengi bilir, seri takımı:
    /// kazanan renk o maçta o renkte oynayan takıma çevrilir, sonra renkler değişir.
    /// </summary>
    /// <param name="match">Oynanan maçın kimliği (simülasyon kaydı; seri sadece saklar).</param>
    internal Result RecordGame(Side winner, MatchId match)
    {
        if (!Enum.IsDefined(winner))
            throw new ArgumentOutOfRangeException(nameof(winner), winner, "Bilinmeyen taraf.");

        if (IsFinished)
            return Result.Fail(SeriesErrors.SeriesFinished);
        if (CurrentDraft?.Outcome is not { } outcome)
            return Result.Fail(SeriesErrors.DraftNotCompleted);

        // Sıra önemli: maç listeye eklenince BlueTeam bir sonraki maça göre döner,
        // o yüzden kazanan takım eklemeden önce hesaplanır.
        var winningTeam = TeamOn(winner);
        _games.Add(new SeriesGame(GameNumber, BlueTeam, winningTeam, outcome, match));
        _wins[winningTeam]++;
        CurrentDraft = null;
        return Result.Ok();
    }
}
