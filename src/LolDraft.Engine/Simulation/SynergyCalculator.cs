namespace LolDraft.Engine.Simulation;

/// <summary>Draft'ın kendisinden gelen başlangıç puanı (DESIGN.md §4.4).</summary>
internal static class SynergyCalculator
{
    private const int ComboValue = 2;

    // Kombo tablosu (prototip): iki şampiyon aynı takımdaysa +2. 40 şampiyonla genişleyecek.
    private static readonly (string A, string B)[] Combos =
    [
        ("kayle", "lulu"),   // Lulu koruması + hiper taşıyıcı
        ("leona", "draven"), // Leona engage + Draven hasarı
    ];

    public static int Score(TeamDraft team)
    {
        var champions = team.Champions;
        var score = 0;

        // Frontline: en az bir şampiyonda fl ≥ 2 → +3, yoksa −4 (önde duracak kimse yok).
        score += champions.Any(c => c.Frontline >= 2) ? 3 : -4;

        // Engage: en az bir şampiyonda en ≥ 2 → +2.
        if (champions.Any(c => c.Engage >= 2))
            score += 2;

        // Hasar dengesi: ortalama AP payı %30-%70 → +2; tek tipe kaymışsa −3 (rakip tek tip savunma dizer).
        var apShare = champions.Average(c => c.ApShare);
        score += apShare is >= 0.3 and <= 0.7 ? 2 : -3;

        var ids = champions.Select(c => c.Id).ToHashSet();
        score += Combos.Count(combo => ids.Contains(combo.A) && ids.Contains(combo.B)) * ComboValue;

        return score;
    }
}
