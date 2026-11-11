namespace LolDraft.Engine.Simulation;

/// <summary>
/// Altından gerçek güce (DESIGN.md §16.5): potansiyel ancak altınla ortaya çıkar.
/// Altın, aynı roldeki rakiple kıyaslanır; böylece support sırf rolü yüzünden "geride" sayılmaz.
/// </summary>
internal sealed partial class MatchRun
{
    // Altına duyarlılık s = 0,45 + 0,45 × econ (econ 0: 0,45 · 1: 0,9 · 2: 1,35 · 3: 1,8).
    // Faz D: eskiden 0,5 + 0,5 × econ; %10 indirildi (önde olan farkı daha az maça çevirsin). Daha fazlası scale
    // comp'u fazla kurtarır. Deney (DESIGN.md §16.5): herkes 0,9 çok zayıf, herkes 3 kartopu.
    private const double BaseGoldSensitivity = 0.45;
    private const double GoldSensitivityPerEconomy = 0.45;
    private const double MinGoldFactor = 0.5;
    private const double MaxGoldFactor = 1.4;

    // Gerçek güç:
    //   r     = altın / (rolün iki oyuncusunun ortalama altını)     1 = başa baş, 1,1 = rakibinden %22 fazla
    //   G_eff = G_pot(dk) × clamp(1 + s × (r − 1), 0,5, 1,4)
    // Örnek r = 1,1: Jinx (econ 3) ×1,18, Draven (econ 2) ×1,135, Leona (econ 0) ×1,045.
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
    // Motor şampiyonları hep takım listelerinden alır; önce referansla eşlenir (record ==, her çağrıda bütün alanları ve
    // etiketleri karşılaştırırdı, en sık çağrılan yol). Bulunamazsa değerle (dışarıdan gelen kopya için).
    private ChampionProfile Counterpart(ChampionProfile champion)
    {
        var blue = _blue.Champions;
        var red = _red.Champions;
        for (var i = 0; i < blue.Count; i++)
        {
            if (ReferenceEquals(blue[i], champion))
                return red[i];
            if (ReferenceEquals(red[i], champion))
                return blue[i];
        }

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
