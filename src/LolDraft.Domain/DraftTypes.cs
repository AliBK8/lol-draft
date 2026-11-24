namespace LolDraft.Domain;

/// <summary>Yapılmış bir hamle. Zaman aşımında ban boş kalır (<see cref="Champion"/> null).</summary>
public sealed record DraftAction(int Step, Side Side, DraftActionKind Kind, ChampionId? Champion, bool ByTimeout);

/// <summary>
/// Geçerli bir ön izleme (§8.32): <paramref name="Side"/> takımı <paramref name="Step"/>. adımda
/// <paramref name="Champion"/>'u gösteriyor (null: ön izleme temizlendi). Hamle değildir, durumu değiştirmez.
/// </summary>
public sealed record DraftPreview(Side Side, int Step, ChampionId? Champion);

/// <summary>Adım süreleri (DESIGN.md §2: her adım 30 sn; §4.10: plan 20 sn).</summary>
public sealed record DraftTimings
{
    private static readonly TimeSpan DefaultPlan = TimeSpan.FromSeconds(20);

    public DraftTimings(TimeSpan turn, TimeSpan swap)
        : this(turn, swap, DefaultPlan)
    {
    }

    public DraftTimings(TimeSpan turn, TimeSpan swap, TimeSpan plan)
    {
        if (turn <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(turn), turn, "Süre pozitif olmalı.");
        if (swap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(swap), swap, "Süre pozitif olmalı.");
        if (plan <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(plan), plan, "Süre pozitif olmalı.");

        Turn = turn;
        Swap = swap;
        Plan = plan;
    }

    public static DraftTimings Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), DefaultPlan);

    public TimeSpan Turn { get; }
    public TimeSpan Swap { get; }
    public TimeSpan Plan { get; }
}

/// <summary>
/// Oyun planı (DESIGN.md §4.10). Domain motoru bilmez: Application motorun GamePlan'ına çevirir (adlar aynı).
/// Seçmeyen <see cref="Balanced"/> oynar.
/// </summary>
public enum MatchPlan
{
    Balanced,
    DragonLate,
    EarlyPressure,
    BotPressure,
    Split,
    FastFinish,
    Invade,
}

/// <summary>
/// Seriden gelen fearless kilitleri: serinin önceki maçlarında seçilen şampiyonlar.
/// İki takım için de kilitlidir (full fearless; soft fearless yok, karar 2026-11-10).
/// </summary>
public sealed record FearlessLocks
{
    public FearlessLocks(IEnumerable<ChampionId> champions)
    {
        ArgumentNullException.ThrowIfNull(champions);

        // Kopya: dışarıdaki liste sonradan değişse de draft etkilenmez.
        Champions = champions.ToHashSet();
    }

    public static FearlessLocks None { get; } = new([]);

    public IReadOnlySet<ChampionId> Champions { get; }

    public bool Contains(ChampionId champion) => Champions.Contains(champion);
}

/// <summary>Zaman aşımındaki rastgele pick için. Gerçekte CSPRNG (Infrastructure), testte sabit.</summary>
public interface IDraftRandom
{
    /// <summary>[0, maxExclusive) aralığında bir sayı.</summary>
    int Next(int maxExclusive);
}

/// <summary>Bir takımın 5 yuvası. Değişmez: swap yeni bir dizilim döndürür.</summary>
public sealed class TeamLineup
{
    private readonly ChampionId[] _slots;

    /// <summary>Şampiyonlar <see cref="Role"/> sırasıyla: top, jungle, mid, bot, support.</summary>
    public TeamLineup(IReadOnlyList<ChampionId> champions)
    {
        ArgumentNullException.ThrowIfNull(champions);
        if (champions.Count != 5)
            throw new ArgumentException("Dizilimde tam 5 şampiyon olmalı.", nameof(champions));
        if (champions.Distinct().Count() != 5)
            throw new ArgumentException("Aynı şampiyon iki yuvada olamaz.", nameof(champions));

        _slots = [.. champions];
    }

    public ChampionId this[Role role] => _slots[(int)role];

    public IReadOnlyList<ChampionId> Champions => _slots;

    public TeamLineup Swap(Role a, Role b)
    {
        var slots = (ChampionId[])_slots.Clone();
        (slots[(int)a], slots[(int)b]) = (slots[(int)b], slots[(int)a]);
        return new TeamLineup(slots);
    }
}

/// <summary>
/// Biten draft'ın çıktısı. Application bunu kataloğa bakarak motorun TeamDraft'ına çevirir. Planlar burada: maç
/// oynanırken motora gider, replay'e yazılır (rakibin planı maç sonunda görünür).
/// </summary>
public sealed record DraftResult(
    TeamLineup Blue, TeamLineup Red, IReadOnlyList<DraftAction> Actions, MatchPlan BluePlan, MatchPlan RedPlan);
