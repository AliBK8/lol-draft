namespace LolDraft.Engine.Simulation;

/// <summary>Altının nereden geldiği (DESIGN.md §16.4). Maç sonu karnesindeki altın kaynakları dökümü buna dayanır.</summary>
internal enum GoldSource
{
    Farm,
    SupportItem,
    Kill,
    Bounty,
    Assist,
    Plate,
    Tower,
    Objective,
}

/// <summary>
/// Her şampiyonun altını ve CS'si (DESIGN.md §16.4): başlangıç + yaşarken pasif gelir + kazanılan.
/// Pasif gelir saniye saniye eklenmez; sorulduğu an "şu ana kadar kaç saniye yaşadı" üzerinden hesaplanır.
/// </summary>
internal sealed class GoldLedger
{
    public const double StartingGold = 500;
    public const double IncomePerSecond = 2;

    private static readonly int SourceCount = Enum.GetValues<GoldSource>().Length;

    // Sadece arama; üzerinde döngü kurulmadığı için Dictionary sırası deterministikliği etkilemez.
    private readonly Dictionary<string, double[]> _earned = [];
    private readonly Dictionary<string, double> _cs = [];
    private readonly Dictionary<string, List<(double From, double Until)>> _deaths = [];

    public void Earn(ChampionProfile champion, GoldSource source, double amount)
    {
        if (!_earned.TryGetValue(champion.Id, out var bySource))
            _earned[champion.Id] = bySource = new double[SourceCount];
        bySource[(int)source] += amount;
    }

    // Kesilen minyon/kamp; kesirli tutulur (10 sn'lik dilimlerde 1,4 CS gibi), gösterimde aşağı yuvarlanır.
    public void AddCs(ChampionProfile champion, double cs) => _cs[champion.Id] = Cs(champion) + cs;

    public double Cs(ChampionProfile champion) => _cs.GetValueOrDefault(champion.Id);

    /// <summary>Ölüm aralığı [from, until): bu sürede pasif gelir yok.</summary>
    public void Died(ChampionProfile champion, double from, double until)
    {
        if (!_deaths.TryGetValue(champion.Id, out var list))
            _deaths[champion.Id] = list = [];
        list.Add((from, until));
    }

    /// <summary>Son yeniden doğma anı; hiç ölmediyse maç başı (0).</summary>
    public double AliveSince(ChampionProfile champion) =>
        _deaths.TryGetValue(champion.Id, out var list) ? list[^1].Until : 0;

    // altın(t) = 500 + 2 × (t − t'ye kadar ölü geçen süre) + kazanılan
    // Ölüm aralıkları t'de kırpılır: hâlâ ölüyse sadece t'ye kadarki kısmı düşülür.
    public double GoldAt(ChampionProfile champion, double t)
    {
        var dead = 0.0;
        if (_deaths.TryGetValue(champion.Id, out var list))
        {
            foreach (var (from, until) in list)
                dead += Math.Max(0, Math.Min(until, t) - from);
        }

        return StartingGold + IncomePerSecond * (t - dead) + Earned(champion);
    }

    public double Earned(ChampionProfile champion, GoldSource source) =>
        _earned.TryGetValue(champion.Id, out var bySource) ? bySource[(int)source] : 0;

    // Kaynak sırasıyla toplar (deterministik toplama sırası).
    private double Earned(ChampionProfile champion) =>
        _earned.TryGetValue(champion.Id, out var bySource) ? ScoreSheet.SumInOrder(bySource) : 0;
}
