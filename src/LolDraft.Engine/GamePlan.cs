namespace LolDraft.Engine;

/// <summary>
/// Oyun planı (DESIGN.md §4.10): draft'tan sonra her takımın gizlice seçtiği maç planı. Güç vermez; motorun ayar
/// düğmelerini o takım için çevirir, her kazancın bir bedeli var. Seçmeyen <see cref="Balanced"/> oynar.
/// </summary>
public enum GamePlan
{
    /// <summary>Düğmelere dokunmaz: plansız motorla bit bit aynı.</summary>
    Balanced,

    /// <summary>Ejder odaklı, late'e götür.</summary>
    DragonLate,

    /// <summary>Erken baskı: fazladan gank, erken çatışma.</summary>
    EarlyPressure,

    /// <summary>Bot lane baskısı: gank'lar bot'a.</summary>
    BotPressure,

    /// <summary>Split: yan koridorda baskı.</summary>
    Split,

    /// <summary>Hızlı bitir: kuşatmaya erken git.</summary>
    FastFinish,

    /// <summary>Ormancı istilası: 4. dakikada rakip ormana gir.</summary>
    Invade,
}

/// <summary>Planın draft'a uyumu: planın kazancı uyana ×1,25, nötre ×1, uymayana ×0,75 (bedel hep tam).</summary>
public enum PlanFit
{
    Poor,
    Neutral,
    Good,
}
