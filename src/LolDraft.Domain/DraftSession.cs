namespace LolDraft.Domain;

/// <summary>
/// Tek maçın draft'ı (DESIGN.md §2, §8.3): 20 ban/pick adımı, ardından swap.
/// Her hamle önce tamamen doğrulanır, durum en sonda tek seferde değişir;
/// geçersiz hamle <see cref="Result.Fail"/> döner ve hiçbir şey değişmez.
/// Saat ve rastgelelik dışarıdan gelir; sınıf ağ, DB ya da zamanlayıcı bilmez.
/// </summary>
public sealed class DraftSession
{
    // Her pick'te en az bir uygun şampiyon kalsın: 10 ban + 10 pick.
    internal const int MinimumAvailable = 20;

    private readonly HashSet<ChampionId> _pool;
    private readonly FearlessLocks _locks;
    private readonly DraftTimings _timings;
    private readonly List<DraftAction> _actions = [];
    private readonly HashSet<ChampionId> _taken = [];
    private readonly Dictionary<Side, List<ChampionId>> _picks = new() { [Side.Blue] = [], [Side.Red] = [] };
    private readonly Dictionary<Side, TeamLineup> _lineups = [];
    private readonly HashSet<Side> _lockedLineups = [];
    private int _stepIndex;

    private DraftSession(HashSet<ChampionId> pool, FearlessLocks locks, DraftTimings timings, Instant now)
    {
        _pool = pool;
        _locks = locks;
        _timings = timings;
        Deadline = now + timings.Turn;
    }

    public static Result<DraftSession> Start(
        IEnumerable<ChampionId> pool, FearlessLocks locks, DraftTimings timings, Instant now)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(timings);

        var poolSet = pool.ToHashSet();

        // Kilitler düşüldükten sonra en az 20 şampiyon kalmalı; yoksa bir pick'te
        // seçilecek şampiyon kalmayabilir (ör. 40'lık havuzda fearless bo5'in 4. maçı: 30 kilit).
        if (poolSet.Count(c => !locks.Contains(c)) < MinimumAvailable)
            return Result<DraftSession>.Fail(DraftErrors.PoolTooSmall);

        return Result<DraftSession>.Ok(new DraftSession(poolSet, locks, timings, now));
    }

    public DraftPhase Phase =>
        CurrentStep?.Phase ?? (Outcome is null ? DraftPhase.Swap : DraftPhase.Completed);

    /// <summary>Sıradaki ban/pick adımı; swap ve sonrasında null.</summary>
    public DraftStep? CurrentStep => _stepIndex < DraftOrder.Steps.Count ? DraftOrder.Steps[_stepIndex] : null;

    public Side? Turn => CurrentStep?.Side;

    /// <summary>Şu anki adımın (swap'ta tüm fazın) bitiş anı; draft bitince null.</summary>
    public Instant? Deadline { get; private set; }

    public IReadOnlyList<DraftAction> Actions => _actions;

    /// <summary>Draft bitince dolar.</summary>
    public DraftResult? Outcome { get; private set; }

    /// <summary>Swap fazından itibaren takımın dizilimi; öncesinde null.</summary>
    public TeamLineup? Lineup(Side side) => _lineups.GetValueOrDefault(side);

    public bool IsLineupLocked(Side side) => _lockedLineups.Contains(side);

    public Result Ban(Side side, ChampionId champion, Instant now) => Act(side, DraftActionKind.Ban, champion, now);

    public Result Pick(Side side, ChampionId champion, Instant now) => Act(side, DraftActionKind.Pick, champion, now);

    /// <summary>Swap fazında iki yuvadaki şampiyonların yerini değiştirir (arayüzde top'a, sonra adc'ye tıklamak).</summary>
    public Result SwapRoles(Side side, Role a, Role b, Instant now)
    {
        var check = CheckSwap(side, now);
        if (check.IsFailure)
            return check;
        if (!Enum.IsDefined(a) || !Enum.IsDefined(b))
            return Result.Fail(DraftErrors.InvalidRole);
        if (a == b)
            return Result.Fail(DraftErrors.SameRole);

        _lineups[side] = _lineups[side].Swap(a, b);
        return Result.Ok();
    }

    /// <summary>Takım dizilimini onaylar; iki takım da onaylayınca draft biter.</summary>
    public Result LockLineup(Side side, Instant now)
    {
        var check = CheckSwap(side, now);
        if (check.IsFailure)
            return check;

        _lockedLineups.Add(side);
        if (_lockedLineups.Count == 2)
            Complete();
        return Result.Ok();
    }

    /// <summary>
    /// Sunucu sayacı süre dolunca çağırır: ban adımında boş ban, pick adımında uygun
    /// şampiyonlardan rastgele biri, swap'ta o anki dizilimler kabul edilir.
    /// </summary>
    public Result Timeout(Instant now, IDraftRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Deadline is not { } deadline)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now < deadline)
            return Result.Fail(DraftErrors.NotExpired);

        if (CurrentStep is not { } step)
        {
            Complete();
            return Result.Ok();
        }

        if (step.Kind == DraftActionKind.Ban)
        {
            Apply(step, null, byTimeout: true, now);
            return Result.Ok();
        }

        // Sıralı liste: aynı zar aynı şampiyonu versin (HashSet sırası garanti değil).
        var available = _pool
            .Where(c => !_taken.Contains(c) && !_locks.Contains(c))
            .OrderBy(c => c.Value, StringComparer.Ordinal)
            .ToList();

        Apply(step, available[random.Next(available.Count)], byTimeout: true, now);
        return Result.Ok();
    }

    private Result Act(Side side, DraftActionKind kind, ChampionId champion, Instant now)
    {
        ThrowIfUnknown(side);

        // Kontrol sırası: önce faz ve süre, sonra sıra, en son şampiyon.
        if (CurrentStep is not { } step)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now >= Deadline)
            return Result.Fail(DraftErrors.TurnExpired);
        if (step.Side != side)
            return Result.Fail(DraftErrors.NotYourTurn);
        if (step.Kind != kind)
            return Result.Fail(DraftErrors.WrongAction);
        if (!_pool.Contains(champion))
            return Result.Fail(DraftErrors.NotInPool);
        if (_taken.Contains(champion))
            return Result.Fail(DraftErrors.ChampionUnavailable);

        // Kilitli şampiyon seçilemez; ban'lamak da anlamsız (zaten kimse seçemez), o da olmaz.
        if (_locks.Contains(champion))
            return Result.Fail(DraftErrors.LockedByFearless);

        Apply(step, champion, byTimeout: false, now);
        return Result.Ok();
    }

    private void Apply(DraftStep step, ChampionId? champion, bool byTimeout, Instant now)
    {
        _actions.Add(new DraftAction(_stepIndex, step.Side, step.Kind, champion, byTimeout));
        if (champion is { } taken)
        {
            _taken.Add(taken);
            if (step.Kind == DraftActionKind.Pick)
                _picks[step.Side].Add(taken);
        }

        _stepIndex++;
        if (CurrentStep is not null)
        {
            Deadline = now + _timings.Turn;
            return;
        }

        // 20 adım bitti → swap: pick sırası yuva sırası olur (1. pick top, 2. pick jungle...).
        foreach (var side in Enum.GetValues<Side>())
            _lineups[side] = new TeamLineup(_picks[side]);
        Deadline = now + _timings.Swap;
    }

    private Result CheckSwap(Side side, Instant now)
    {
        ThrowIfUnknown(side);

        if (Phase != DraftPhase.Swap)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now >= Deadline)
            return Result.Fail(DraftErrors.TurnExpired);
        if (_lockedLineups.Contains(side))
            return Result.Fail(DraftErrors.LineupLocked);
        return Result.Ok();
    }

    private void Complete()
    {
        Outcome = new DraftResult(_lineups[Side.Blue], _lineups[Side.Red], _actions.ToArray());
        Deadline = null;
    }

    // Taraf istemciden değil, sunucunun koltuk eşlemesinden gelir; bilinmeyen değer programcı hatasıdır.
    private static void ThrowIfUnknown(Side side)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), side, "Bilinmeyen taraf.");
    }
}
