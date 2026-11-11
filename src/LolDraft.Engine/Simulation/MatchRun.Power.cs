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

    // Şampiyon → rol dışı cezası (OffRole; 0 = kendi rolünde). Kimlikle: iki takımda aynı şampiyon olamaz (MatchInput).
    private readonly Dictionary<string, double> _rolePenalty = [];

    // Kurucu çağırır: oynanan yuva TeamDraft sırasıdır (top, jungle, mid, bot, support = ChampionRole sırası).
    private void AssignRoles()
    {
        foreach (var team in (TeamDraft[])[_blue, _red])
        {
            for (var i = 0; i < team.Champions.Count; i++)
                _rolePenalty[team.Champions[i].Id] = OffRole.Penalty(team.Champions[i], (ChampionRole)i);
        }
    }

    private double RolePenalty(ChampionProfile champion) => _rolePenalty.GetValueOrDefault(champion.Id);

    // Gerçek güç:
    //   r     = altın / (rolün iki oyuncusunun ortalama altını)     1 = başa baş, 1,1 = rakibinden %22 fazla
    //   G_eff = G_pot(dk) × (1 − rol dışı cezası) × clamp(1 + s × (r − 1), 0,5, 1,4)
    // Örnek r = 1,1: Jinx (econ 3) ×1,18, Draven (econ 2) ×1,135, Leona (econ 0) ×1,045.
    // Rol dışı: Syndra (7-8-7) top'ta %20 cezayla 5. dk'da 7 → 5,6 (rakip Darius 8; K1 range baskısı +0,3 ile koridor
    // hâlâ hafif Syndra'nın). Kendi rolünde çarpan tam 1: sonuç cezasız hesapla bit bit aynı.
    private double EffectivePower(ChampionProfile champion, int minute, double t) =>
        champion.PowerAt(minute) * (1 - RolePenalty(champion))
        * GoldFactor(_gold.GoldAt(champion, t), _gold.GoldAt(Counterpart(champion), t), champion.Economy);

    // Rol dışı kumarın oynaklığı: lane fazında, verilenlerden biri rol dışıysa gürültü × 1,5 (OffRole). Ortalamayı
    // değiştirmez; zar sayısını da değiştirmez (aynı NextNormal'ın çarpanı), değilse tam 1.
    private double Volatility(int minute, params ChampionProfile[] champions) =>
        minute < OffRole.VolatileUntilMinute && champions.Any(c => RolePenalty(c) > 0) ? OffRole.NoiseFactor : 1;

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
