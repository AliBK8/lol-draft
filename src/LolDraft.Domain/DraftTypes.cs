namespace LolDraft.Domain;

/// <summary>Yapılmış bir hamle. Zaman aşımında ban boş kalır (<see cref="Champion"/> null).</summary>
public sealed record DraftAction(int Step, Side Side, DraftActionKind Kind, ChampionId? Champion, bool ByTimeout);

/// <summary>Adım süreleri (DESIGN.md §2: her adım 30 sn).</summary>
public sealed record DraftTimings
{
    public DraftTimings(TimeSpan turn, TimeSpan swap)
    {
        if (turn <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(turn), turn, "Süre pozitif olmalı.");
        if (swap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(swap), swap, "Süre pozitif olmalı.");

        Turn = turn;
        Swap = swap;
    }

    public static DraftTimings Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    public TimeSpan Turn { get; }
    public TimeSpan Swap { get; }
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

/// <summary>Biten draft'ın çıktısı. Application bunu kataloğa bakarak motorun TeamDraft'ına çevirir.</summary>
public sealed record DraftResult(TeamLineup Blue, TeamLineup Red, IReadOnlyList<DraftAction> Actions);
