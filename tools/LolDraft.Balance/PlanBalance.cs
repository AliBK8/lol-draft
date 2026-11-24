using System.Globalization;
using LolDraft.Engine;

namespace LolDraft.Balance;

/// <summary>
/// Oyun planı ölçümü (--plans, DESIGN.md §4.10): her draft çifti rastgele bir plan çiftiyle, ayna oynanır (A Mavi/B
/// Kırmızı, sonra yer ve plan değiştirip aynı seed). Plan zarları ayrı bir üreteçten: draft dizisi normal ölçümle aynı.
/// Hedefler: baskın plan yok (bütün planlara karşı ~%47-53), uyan plan Dengeli'ye karşı ~%53-55, uymayan ~%47,
/// her planın en az bir kötü eşleşmesi (karşıtı) var.
/// </summary>
internal sealed class PlanStats
{
    private static readonly GamePlan[] Plans = Enum.GetValues<GamePlan>();
    private static readonly int Count = Plans.Length;

    // [benim planım, rakibin planı]: maç, kazandığım.
    private readonly int[,] _games = new int[Count, Count];
    private readonly int[,] _wins = new int[Count, Count];

    // [plan, uyum]: Dengeli'ye karşı maç, kazandığı.
    private readonly int[,] _fitGames = new int[Count, 3];
    private readonly int[,] _fitWins = new int[Count, 3];

    // [plan, uyum]: draft'ların kaçında (her takım ayrı sayılır).
    private readonly int[,] _fitSeen = new int[Count, 3];
    private int _teams;

    private double _minuteSum;
    private int _matches;

    public void Add(TeamDraft blue, TeamDraft red, GamePlan bluePlan, GamePlan redPlan, MatchResult result)
    {
        var blueWon = result.End.Winner == TeamSide.Blue;
        Record(blue, bluePlan, redPlan, blueWon);
        Record(red, redPlan, bluePlan, !blueWon);
        _minuteSum += result.End.Minute;
        _matches++;
    }

    private void Record(TeamDraft team, GamePlan mine, GamePlan theirs, bool won)
    {
        var (m, t) = ((int)mine, (int)theirs);
        _games[m, t]++;
        if (won)
            _wins[m, t]++;

        // Uyum dağılımı: bu takımın draft'ına her plan ne kadar uyuyor (plan seçiminden bağımsız).
        foreach (var plan in Plans)
            _fitSeen[(int)plan, (int)MatchEngine.PlanFit(team, plan)]++;
        _teams++;

        if (theirs == GamePlan.Balanced)
        {
            var fit = (int)MatchEngine.PlanFit(team, mine);
            _fitGames[m, fit]++;
            if (won)
                _fitWins[m, fit]++;
        }
    }

    public void Print(TextWriter w)
    {
        w.WriteLine($"## Oyun planları ({_matches} maç, rastgele plan çifti, ayna; ortalama süre {_minuteSum / _matches:0.0} dk)");
        w.WriteLine("  Plan: bütün planlara karşı · Dengeli'ye karşı uyan / nötr / uymayan");
        foreach (var plan in Plans)
        {
            var p = (int)plan;
            var (games, wins) = (0, 0);
            for (var t = 0; t < Count; t++)
                (games, wins) = (games + _games[p, t], wins + _wins[p, t]);
            string Fit(PlanFit f) => $"{BalanceReport.Pct(_fitWins[p, (int)f], _fitGames[p, (int)f])} ({_fitGames[p, (int)f]})";
            w.WriteLine($"  {Name(plan),-14} {BalanceReport.Pct(wins, games) + BalanceReport.Margin(wins, games),-14}"
                        + $"  · {Fit(PlanFit.Good),-14} / {Fit(PlanFit.Neutral),-14} / {Fit(PlanFit.Poor)}");
        }

        w.WriteLine("  hedef: bütün planlara karşı ~%47-53 (baskın yok); Dengeli'ye karşı uyan ~%53-55, uymayan ~%47");
        w.WriteLine();
        w.WriteLine("  Uyum dağılımı (draft'ların yüzde kaçında uyan / nötr / uymayan):");
        foreach (var plan in Plans.Where(p => p != GamePlan.Balanced))
        {
            var p = (int)plan;
            w.WriteLine($"    {Name(plan),-14} {BalanceReport.Pct(_fitSeen[p, 2], _teams),-7} / {BalanceReport.Pct(_fitSeen[p, 1], _teams),-7} / {BalanceReport.Pct(_fitSeen[p, 0], _teams)}");
        }

        w.WriteLine();
        w.WriteLine("  Matris (satır planı sütun planına karşı kazanıyor; ±~2):");
        w.WriteLine("    " + new string(' ', 14) + string.Concat(Plans.Select(p => $"{Short(p),8}")));
        foreach (var mine in Plans)
        {
            var cells = Plans.Select(t => $"{BalanceReport.Pct(_wins[(int)mine, (int)t], _games[(int)mine, (int)t]),8}");
            w.WriteLine($"    {Name(mine),-14}" + string.Concat(cells));
        }
    }

    private static string Name(GamePlan plan) => plan switch
    {
        GamePlan.Balanced => "Dengeli",
        GamePlan.DragonLate => "Ejder & late",
        GamePlan.EarlyPressure => "Erken baskı",
        GamePlan.BotPressure => "Bot baskısı",
        GamePlan.Split => "Split",
        GamePlan.FastFinish => "Hızlı bitir",
        GamePlan.Invade => "İstila",
        _ => plan.ToString(),
    };

    private static string Short(GamePlan plan) => plan switch
    {
        GamePlan.Balanced => "Deng",
        GamePlan.DragonLate => "Ejder",
        GamePlan.EarlyPressure => "Erken",
        GamePlan.BotPressure => "Bot",
        GamePlan.Split => "Split",
        GamePlan.FastFinish => "Hızlı",
        GamePlan.Invade => "İstila",
        _ => plan.ToString(),
    };

    /// <summary>Plan zarları: draft dizisinden ayrı, aynı --seed aynı plan dizisi.</summary>
    public static GamePlan Random(Random random) => Plans[random.Next(Count)];
}
