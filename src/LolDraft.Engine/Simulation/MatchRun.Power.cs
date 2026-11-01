namespace LolDraft.Engine.Simulation;

/// <summary>
/// Altından gerçek güce (DESIGN.md §16.5): potansiyel ancak altınla ortaya çıkar.
/// Altın, aynı roldeki rakiple kıyaslanır; böylece support sırf rolü yüzünden "geride" sayılmaz.
/// </summary>
internal sealed partial class MatchRun
{
    // Altına duyarlılık s. Şampiyon modeli v2'de econ'dan gelecek (econ 0: 0,5 · 1: 1 · 2: 1,5 · 3: 2);
    // o zamana kadar herkes econ 2. Deney (DESIGN.md §16.13 adım 4): 0,9 çok zayıf, 3 kartopu; 2 de aday.
    private const double GoldSensitivity = 1.5;
    private const double MinGoldFactor = 0.5;
    private const double MaxGoldFactor = 1.4;

    // Gerçek güç:
    //   r     = altın / (rolün iki oyuncusunun ortalama altını)     1 = başa baş, 1,1 = rakibinden %22 fazla
    //   G_eff = G_pot(dk) × clamp(1 + s × (r − 1), 0,5, 1,4)
    // Örnek: Renekton 5.150 vs Kayle 4.050 altın → r 1,12 / 0,88 → güç ×1,18 / ×0,82.
    private double EffectivePower(ChampionProfile champion, int minute, double t) =>
        champion.PowerAt(minute) * GoldFactor(_gold.GoldAt(champion, t), _gold.GoldAt(Counterpart(champion), t));

    internal static double GoldFactor(double gold, double counterpartGold)
    {
        var ratio = gold / ((gold + counterpartGold) / 2);
        return Math.Clamp(1 + GoldSensitivity * (ratio - 1), MinGoldFactor, MaxGoldFactor);
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
