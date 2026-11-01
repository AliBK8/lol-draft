namespace LolDraft.Engine;

/// <summary>
/// Motorun bir şampiyondan bilmesi gerekenler (DESIGN.md §3). Kurucu doğrular; aralık dışı değerle nesne oluşmaz.
/// </summary>
public sealed record ChampionProfile
{
    public ChampionProfile(string id, string name, int early, int mid, int late, int frontline, int engage, double apShare)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Early = InRange(early, 1, 10, nameof(early));
        Mid = InRange(mid, 1, 10, nameof(mid));
        Late = InRange(late, 1, 10, nameof(late));
        Frontline = InRange(frontline, 0, 3, nameof(frontline));
        Engage = InRange(engage, 0, 3, nameof(engage));

        if (!(apShare is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(apShare), apShare, "AP payı 0 ile 1 arasında olmalı.");
        ApShare = apShare;
    }

    public string Id { get; }
    public string Name { get; }
    public int Early { get; }
    public int Mid { get; }
    public int Late { get; }
    public int Frontline { get; }
    public int Engage { get; }
    public double ApShare { get; }

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
