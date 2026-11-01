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

    // Faz gücü G(şampiyon, dk) (DESIGN.md §4.3): dk < 14 erken, 14 ≤ dk < 25 orta, dk ≥ 25 geç.
    internal int PowerAt(int minute) => minute < 14 ? Early : minute < 25 ? Mid : Late;

    private static int InRange(int value, int min, int max, string name) =>
        value >= min && value <= max
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"{min} ile {max} arasında olmalı.");
}
