namespace LolDraft.Engine.Simulation;

/// <summary>
/// Altından gerçek güce (DESIGN.md §16.5): potansiyel ancak altınla ortaya çıkar.
/// Altın, aynı roldeki rakiple kıyaslanır; böylece support sırf rolü yüzünden "geride" sayılmaz.
/// </summary>
internal sealed partial class MatchRun
{
    // Altına duyarlılık s = 0,5 + 0,5 × econ (econ 0: 0,5 · 1: 1 · 2: 1,5 · 3: 2). econ 2 = faz A'daki herkesin s'i.
    // Deney (DESIGN.md §16.5): herkes 0,9 çok zayıf, herkes 3 kartopu.
    private const double BaseGoldSensitivity = 0.5;
    private const double GoldSensitivityPerEconomy = 0.5;
    private const double MinGoldFactor = 0.5;
    private const double MaxGoldFactor = 1.4;

    // Gerçek güç:
    //   r     = altın / (rolün iki oyuncusunun ortalama altını)     1 = başa baş, 1,1 = rakibinden %22 fazla
    //   G_eff = G_pot(dk) × clamp(1 + s × (r − 1), 0,5, 1,4)
    // Örnek r = 1,1: Jinx (econ 3) ×1,20, Draven (econ 2) ×1,15, Leona (econ 0) ×1,05.
    private double EffectivePower(ChampionProfile champion, int minute, double t) =>
        champion.PowerAt(minute)
        * GoldFactor(_gold.GoldAt(champion, t), _gold.GoldAt(Counterpart(champion), t), champion.Economy);

    internal static double GoldFactor(double gold, double counterpartGold, int economy)
    {
        var ratio = gold / ((gold + counterpartGold) / 2);
        var sensitivity = BaseGoldSensitivity + GoldSensitivityPerEconomy * economy;
        return Math.Clamp(1 + sensitivity * (ratio - 1), MinGoldFactor, MaxGoldFactor);
    }

    // Toplam gerçek güç; sırayla toplanır (deterministik toplama sırası).
    private double EffectivePower(IEnumerable<ChampionProfile> champions, int minute, double t)
    {
        var total = 0.0;
        foreach (var champion in champions)
            total += EffectivePower(champion, minute, t);
        return total;
    }

    // Aynı roldeki rakip: takım sırası (top, jungle, mid, bot, support) iki takımda aynı.
    private ChampionProfile Counterpart(ChampionProfile champion)
    {
        var blue = _blue.Champions;
        var red = _red.Champions;
        for (var i = 0; i < blue.Count; i++)
        {
            if (blue[i] == champion)
                return red[i];
            if (red[i] == champion)
                return blue[i];
        }

        throw new ArgumentException($"{champion.Id} bu maçta yok.", nameof(champion));
    }
}
