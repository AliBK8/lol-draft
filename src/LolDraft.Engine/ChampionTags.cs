namespace LolDraft.Engine;

/// <summary>
/// Şampiyonun takım içindeki işleri, 0-3 (DESIGN.md §16.2). Kurallar (§16.8) bunlara bakar.
/// Verilmeyen etiket 0'dır; kurucu doğrular, aralık dışı değerle nesne oluşmaz.
/// </summary>
public sealed record ChampionTags
{
    public ChampionTags(
        int frontline = 0, int engage = 0, int disengage = 0, int peel = 0, int poke = 0,
        int pick = 0, int dive = 0, int split = 0, int mobility = 0)
    {
        Frontline = InRange(frontline, nameof(frontline));
        Engage = InRange(engage, nameof(engage));
        Disengage = InRange(disengage, nameof(disengage));
        Peel = InRange(peel, nameof(peel));
        Poke = InRange(poke, nameof(poke));
        Pick = InRange(pick, nameof(pick));
        Dive = InRange(dive, nameof(dive));
        Split = InRange(split, nameof(split));
        Mobility = InRange(mobility, nameof(mobility));
    }

    /// <summary>fl: önde durur, hasar emer.</summary>
    public int Frontline { get; }

    /// <summary>en: savaşı başlatır.</summary>
    public int Engage { get; }

    /// <summary>dis: rakip engage'ini boşa çıkarır.</summary>
    public int Disengage { get; }

    /// <summary>peel: taşıyıcıyı korur.</summary>
    public int Peel { get; }

    /// <summary>poke: uzaktan aşındırır.</summary>
    public int Poke { get; }

    /// <summary>pick: tek kişiyi yakalar.</summary>
    public int Pick { get; }

    /// <summary>dive: arka hatta atlar.</summary>
    public int Dive { get; }

    /// <summary>split: yan koridorda tek başına baskı kurar.</summary>
    public int Split { get; }

    /// <summary>mob: hareket kabiliyeti.</summary>
    public int Mobility { get; }

    private static int InRange(int value, string name) =>
        value is >= 0 and <= 3
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Etiket 0 ile 3 arasında olmalı.");
}
