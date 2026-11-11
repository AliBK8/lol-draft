namespace LolDraft.Engine;

/// <summary>
/// Motorun bir şampiyondan bilmesi gerekenler (DESIGN.md §16.2). Kurucu doğrular; aralık dışı değerle nesne oluşmaz.
/// </summary>
public sealed record ChampionProfile
{
    // Doğal roller bit olarak (bit i = ChampionRole i). Liste değil: record eşitliği değerle karşılaştırsın.
    // 0 = rol bilgisi yok, her rolde oynar (test ve örnek profilleri).
    private readonly int _roleMask;

    /// <param name="roles">Doğal roller (data/champions.json'daki roles). Boş ya da null: her rolde oynar, ceza yok.</param>
    public ChampionProfile(
        string id, string name, AttackRange range, ChampionClass primaryClass, ChampionClass? secondaryClass,
        int early, int mid, int late, int economy, double apShare, ChampionTags tags,
        IEnumerable<ChampionRole>? roles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tags);

        if (!Enum.IsDefined(range))
            throw new ArgumentOutOfRangeException(nameof(range), range, "Bilinmeyen menzil.");
        if (!Enum.IsDefined(primaryClass))
            throw new ArgumentOutOfRangeException(nameof(primaryClass), primaryClass, "Bilinmeyen sınıf.");
        if (secondaryClass is { } secondary && (!Enum.IsDefined(secondary) || secondary == primaryClass))
            throw new ArgumentOutOfRangeException(nameof(secondaryClass), secondaryClass, "İkincil sınıf geçerli ve ana sınıftan farklı olmalı.");

        Id = id;
        Name = name;
        Range = range;
        PrimaryClass = primaryClass;
        SecondaryClass = secondaryClass;
        Early = InRange(early, 1, 10, nameof(early));
        Mid = InRange(mid, 1, 10, nameof(mid));
        Late = InRange(late, 1, 10, nameof(late));
        Economy = InRange(economy, 0, 3, nameof(economy));

        if (!(apShare is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(apShare), apShare, "AP payı 0 ile 1 arasında olmalı.");
        ApShare = apShare;
        Tags = tags;

        foreach (var role in roles ?? [])
        {
            if (!Enum.IsDefined(role))
                throw new ArgumentOutOfRangeException(nameof(roles), role, "Bilinmeyen rol.");
            _roleMask |= 1 << (int)role;
        }
    }

    public string Id { get; }
    public string Name { get; }
    public AttackRange Range { get; }

    /// <summary>Ana sınıf; şampiyonun kimliği.</summary>
    public ChampionClass PrimaryClass { get; }

    public ChampionClass? SecondaryClass { get; }

    /// <summary>Güç potansiyeli (1-10); altınla gerçekleşir.</summary>
    public int Early { get; }

    public int Mid { get; }
    public int Late { get; }

    /// <summary>econ: altın/item bağımlılığı. 0 = altınsız da işler, 3 = item'sız işe yaramaz.</summary>
    public int Economy { get; }

    public double ApShare { get; }
    public ChampionTags Tags { get; }

    /// <summary>Doğal roller, yuva sırasıyla. Boşsa rol bilgisi yok: her rolde oynar.</summary>
    public IReadOnlyList<ChampionRole> Roles =>
        [.. Enum.GetValues<ChampionRole>().Where(role => (_roleMask & (1 << (int)role)) != 0)];

    public bool HasClass(ChampionClass championClass) =>
        PrimaryClass == championClass || SecondaryClass == championClass;

    /// <summary>
    /// Hiper taşıyıcı (türetilen tip): nişancı ya da büyücü, econ 3 ve geç güç ≥ 9. Ör. Jinx, Kassadin, Kayle.
    /// Korunan taşıyıcı ve dive kuralları (§16.8 K8, K9) buna bakar.
    /// </summary>
    public bool IsHyperCarry =>
        (HasClass(ChampionClass.Marksman) || HasClass(ChampionClass.Mage)) && Economy == 3 && Late >= 9;

    // Güç potansiyeli G_pot(dk), yumuşak eğri (DESIGN.md §16.3):
    //   dk ≤ 10: erken · 10 → 18: erkenden ortaya doğrusal · 18 → 28: ortadan geçe doğrusal · dk ≥ 28: geç
    // Örnek Kayle (2-6-10): 10. dk 2, 14. dk 4, 18. dk 6, 23. dk 8, 28. dk 10.
    internal double PowerAt(int minute) => minute switch
    {
        <= 10 => Early,
        < 18 => Early + (minute - 10) / 8.0 * (Mid - Early),
        < 28 => Mid + (minute - 18) / 10.0 * (Late - Mid),
        _ => Late,
    };

    private static int InRange(int value, int min, int max, string name) =>
        value >= min && value <= max
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"{min} ile {max} arasında olmalı.");
}
