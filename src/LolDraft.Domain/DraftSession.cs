namespace LolDraft.Domain;

/// <summary>
/// Tek maçın draft'ı (DESIGN.md §2, §8.3): 20 ban/pick adımı, ardından swap, ardından oyun planı (§4.10).
/// Her hamle önce tamamen doğrulanır, durum en sonda tek seferde değişir;
/// geçersiz hamle <see cref="Result.Fail"/> döner ve hiçbir şey değişmez.
/// Saat ve rastgelelik dışarıdan gelir; sınıf ağ, DB ya da zamanlayıcı bilmez.
/// Hamle metotları internal: dışarıdan hamle sadece <see cref="Lobby"/> üzerinden (koltuk → renk, tekrar koruması).
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

    // Plan fazında seçilen planlar (gizli: görünümde sadece seçildi mi). Faz bitince seçmeyen Dengeli.
    private readonly Dictionary<Side, MatchPlan> _plans = [];
    private bool _planning;
    private int _stepIndex;

    // Sıradaki takımın son geçerli ön izlemesi (hangi adımda, hangi şampiyon). Süre dolunca hamle bu olur (LoL'deki
    // gibi: seçili duran şampiyon kilitlenir). Adım numarasıyla tutulur: hamle gelince eski adımınki kendiliğinden
    // geçersiz. Null şampiyon ("Ban yok" ya da seçimi bırakma) da kayıttır: süre dolunca boş ban / rastgele pick.
    private (int Step, ChampionId? Champion)? _hover;

    private DraftSession(HashSet<ChampionId> pool, FearlessLocks locks, DraftTimings timings, Instant now)
    {
        _pool = pool;
        _locks = locks;
        _timings = timings;
        Deadline = now + timings.Turn;
    }

    internal static Result<DraftSession> Start(
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
        CurrentStep?.Phase
        ?? (Outcome is not null ? DraftPhase.Completed : _planning ? DraftPhase.Plan : DraftPhase.Swap);

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

    /// <summary>Takım planını seçti mi. Hangi planı seçtiği gizli (maç bitene kadar sadece kendisi bilir).</summary>
    public bool HasChosenPlan(Side side) => _plans.ContainsKey(side);

    internal Result Ban(Side side, ChampionId champion, Instant now) => Act(side, DraftActionKind.Ban, champion, now);

    internal Result Pick(Side side, ChampionId champion, Instant now) => Act(side, DraftActionKind.Pick, champion, now);

    /// <summary>
    /// Ban adımını bilerek boş geçer (LoL'deki "None", §8.33): sonucu süre dolmasıyla aynı (şampiyonsuz ban),
    /// tek farkı kayıtta zaman aşımı değil. Sadece ban adımında ve sıra bu takımdayken.
    /// </summary>
    internal Result SkipBan(Side side, Instant now)
    {
        ThrowIfUnknown(side);

        var turn = CheckTurn(side, now);
        if (turn.IsFailure)
            return turn;

        var step = CurrentStep!;
        if (step.Kind != DraftActionKind.Ban)
            return Result.Fail(DraftErrors.WrongAction);

        Apply(step, null, byTimeout: false, now);
        return Result.Ok();
    }

    /// <summary>
    /// Ön izleme (rakip görür, §8.32): sırası gelen takım bu şampiyonu şu an ban/pick edebilir mi? Hamleyle aynı
    /// kurallar (faz, süre, sıra, şampiyon); hamle, sıra ve süre değişmez. Geçerliyse adımın numarası döner: istemci ön
    /// izlemeyi sadece o adım sürerken gösterir, hamle gelince eskisi kendiliğinden düşer. Şampiyon null: temizlenir.
    /// Geçerli ön izleme hatırlanır: süre dolarsa hamle o olur (§8.46, <see cref="Timeout"/>).
    /// </summary>
    internal Result<int> Preview(Side side, ChampionId? champion, Instant now)
    {
        ThrowIfUnknown(side);

        var turn = CheckTurn(side, now);
        if (turn.IsFailure)
            return Result<int>.Fail(turn.Error!);

        if (champion is { } value && CheckChampion(value) is { IsFailure: true } unavailable)
            return Result<int>.Fail(unavailable.Error!);

        _hover = (_stepIndex, champion);
        return Result<int>.Ok(_stepIndex);
    }

    /// <summary>Swap fazında iki yuvadaki şampiyonların yerini değiştirir (arayüzde top'a, sonra adc'ye tıklamak).</summary>
    internal Result SwapRoles(Side side, Role a, Role b, Instant now)
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

    /// <summary>Takım dizilimini onaylar; iki takım da onaylayınca plan fazı başlar.</summary>
    internal Result LockLineup(Side side, Instant now)
    {
        var check = CheckSwap(side, now);
        if (check.IsFailure)
            return check;

        _lockedLineups.Add(side);
        if (_lockedLineups.Count == 2)
            StartPlan(now);
        return Result.Ok();
    }

    /// <summary>
    /// Oyun planı (DESIGN.md §4.10): takım planını seçer. Tek seferlik (seçim kilittir); iki takım da seçince draft
    /// biter. Rakip sadece "seçti" görür; hangi plan olduğu maç bitince replay'de açılır.
    /// </summary>
    internal Result ChoosePlan(Side side, MatchPlan plan, Instant now)
    {
        ThrowIfUnknown(side);

        if (Phase != DraftPhase.Plan)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now >= Deadline)
            return Result.Fail(DraftErrors.TurnExpired);
        if (!Enum.IsDefined(plan))
            return Result.Fail(DraftErrors.InvalidPlan);
        if (_plans.ContainsKey(side))
            return Result.Fail(DraftErrors.PlanChosen);

        _plans[side] = plan;
        if (_plans.Count == 2)
            Complete();
        return Result.Ok();
    }

    /// <summary>
    /// Sunucu sayacı süre dolunca çağırır: ban/pick adımında takımın o adımdaki son ön izlemesi (hâlâ uygunsa;
    /// §8.46), yoksa ban'da boş ban, pick'te uygun şampiyonlardan rastgele biri; swap'ta o anki dizilimler kabul edilir (plan fazı başlar), plan fazında seçmeyen
    /// Dengeli oynar (draft biter).
    /// </summary>
    internal Result Timeout(Instant now, IDraftRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Deadline is not { } deadline)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now < deadline)
            return Result.Fail(DraftErrors.NotExpired);

        if (CurrentStep is not { } step)
        {
            if (_planning)
                Complete();
            else
                StartPlan(now);
            return Result.Ok();
        }

        // Seçili duran şampiyon kilitlenir. Ön izlemeden sonra uygunluğu değişmiş olamaz (adım değişmedi) ama ucuz
        // bir güvence: uygun değilse eski kurala düşer.
        if (_hover is { } hover && hover.Step == _stepIndex && hover.Champion is { } hovered
            && CheckChampion(hovered).IsSuccess)
        {
            Apply(step, hovered, byTimeout: true, now);
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
        var turn = CheckTurn(side, now);
        if (turn.IsFailure)
            return turn;

        var step = CurrentStep!;
        if (step.Kind != kind)
            return Result.Fail(DraftErrors.WrongAction);

        var available = CheckChampion(champion);
        if (available.IsFailure)
            return available;

        Apply(step, champion, byTimeout: false, now);
        return Result.Ok();
    }

    // Hamle ve ön izleme ortak: ban/pick adımı açık mı, süresi dolmadı mı, sıra bu takımda mı?
    private Result CheckTurn(Side side, Instant now)
    {
        if (CurrentStep is not { } step)
            return Result.Fail(DraftErrors.PhaseClosed);
        if (now >= Deadline)
            return Result.Fail(DraftErrors.TurnExpired);
        if (step.Side != side)
            return Result.Fail(DraftErrors.NotYourTurn);
        return Result.Ok();
    }

    // Hamle ve ön izleme ortak: şampiyon havuzda mı, alınmamış mı, fearless kilidi yok mu?
    private Result CheckChampion(ChampionId champion)
    {
        if (!_pool.Contains(champion))
            return Result.Fail(DraftErrors.NotInPool);
        if (_taken.Contains(champion))
            return Result.Fail(DraftErrors.ChampionUnavailable);

        // Kilitli şampiyon seçilemez; ban'lamak da anlamsız (zaten kimse seçemez), o da olmaz.
        if (_locks.Contains(champion))
            return Result.Fail(DraftErrors.LockedByFearless);
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

    private void StartPlan(Instant now)
    {
        _planning = true;
        Deadline = now + _timings.Plan;
    }

    // Plan seçmeyen Dengeli oynar.
    private void Complete()
    {
        Outcome = new DraftResult(_lineups[Side.Blue], _lineups[Side.Red], _actions.ToArray(),
            _plans.GetValueOrDefault(Side.Blue, MatchPlan.Balanced), _plans.GetValueOrDefault(Side.Red, MatchPlan.Balanced));
        Deadline = null;
    }

    // Taraf istemciden değil, sunucunun koltuk eşlemesinden gelir; bilinmeyen değer programcı hatasıdır.
    private static void ThrowIfUnknown(Side side)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), side, "Bilinmeyen taraf.");
    }
}
