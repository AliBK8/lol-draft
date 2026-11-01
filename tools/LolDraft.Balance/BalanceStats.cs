using LolDraft.Engine;
using LolDraft.Engine.Events;

namespace LolDraft.Balance;

/// <summary>Maç sonuçlarını biriktirir; rapor bu sayılardan çıkar.</summary>
internal sealed class BalanceStats
{
    // Savaş ihtimali bu kadar sınıra yakınsa "sınıra takıldı" sayılır (1 − 0.9 double'da 0.0999…).
    private const double ClampEpsilon = 1e-9;

    public int Matches { get; private set; }
    public int BlueWins { get; private set; }
    public int SiegeEnds { get; private set; }
    public double MinuteSum { get; private set; }
    public int Kills { get; private set; }
    public int Towers { get; private set; }
    public int Dragons { get; private set; }
    public int Elders { get; private set; }
    public int Throws { get; private set; }
    public int SoulMatches { get; private set; }
    public int SoulWins { get; private set; }
    public int Fights { get; private set; }
    public int ClampedFights { get; private set; }

    // Dakika → o dakikada biten maç sayısı.
    public SortedDictionary<int, int> EndMinutes { get; } = [];

    // Şampiyon → (oynandığı maç, kazandığı maç).
    public Dictionary<string, (string Name, int Games, int Wins)> Champions { get; } = [];

    // Draft gücü farkı dilimi → (maç, güçlü tarafın kazandığı).
    public SortedDictionary<int, (int Games, int StrongerWins)> ByPowerGap { get; } = [];

    // Sinerji farkı (|Mavi − Kırmızı|, DESIGN.md §4.4) → (maç, sinerjisi iyi olanın kazandığı).
    public SortedDictionary<int, (int Games, int BetterWins)> BySynergyGap { get; } = [];

    // Belirgin erken comp (erken − geç farkı büyük olan taraf) → maç, kazandığı, kuşatmayla 35'ten önce bitirdiği.
    public int EarlyCompMatches { get; private set; }
    public int EarlyCompWins { get; private set; }
    public int EarlyCompSiegeWins { get; private set; }

    // Lane fazında ezilen scale comp: belirgin erken comp maçında scale tarafı 14. dk'da ≥ 3.000 geride → kurtarıyor mu?
    public int CrushedScaleMatches { get; private set; }
    public int CrushedScaleWins { get; private set; }

    // Ekonomi (DESIGN.md §16.4).
    public static readonly int[] CurveMinutes = [10, 15, 20];
    public static readonly int[] LeadBuckets = [0, 1500, 3000, 5000];
    private const int CrushedLead = 3000;

    public double LanerCsAt14 { get; private set; }
    public int LanerCsSamples { get; private set; }
    public double TeamLeadAt15 { get; private set; }
    public int TeamLeadSamples { get; private set; }
    public int Shutdowns { get; private set; }
    public double ShutdownGold { get; private set; }
    public int Catches { get; private set; }
    public double CatchGold { get; private set; }

    // (dakika, fark dilimi) → (o dakikaya ulaşan maç, o dakikada altında önde olanın kazandığı).
    public SortedDictionary<(int Minute, int Bucket), (int Games, int LeaderWins)> ByGoldLead { get; } = [];

    public void Add(TeamDraft blue, TeamDraft red, MatchResult result, int powerGapBucketSize, int earlyCompThreshold)
    {
        Matches++;
        var winner = result.End.Winner;
        if (winner == TeamSide.Blue) BlueWins++;
        if (result.End.Kind == MatchEndKind.Siege) SiegeEnds++;
        MinuteSum += result.End.Minute;
        EndMinutes[result.End.Minute] = EndMinutes.GetValueOrDefault(result.End.Minute) + 1;

        var events = result.Events;
        Kills += events.OfType<KillEvent>().Count();
        Towers += events.OfType<TowerEvent>().Count();
        Dragons += events.OfType<ObjectiveEvent>().Count(o => o.Objective == Objective.Dragon);
        Elders += events.OfType<ObjectiveEvent>().Count(o => o.Objective == Objective.Elder);
        Throws += events.OfType<ThrowEvent>().Count();

        if (events.OfType<DragonSoulEvent>().FirstOrDefault() is { } soul)
        {
            SoulMatches++;
            if (soul.Side == winner) SoulWins++;
        }

        AddEconomy(result);

        // Sınıra takılan savaşlar: objektif, nehir çatışması, kuşatma (Nexus/Throw). Gank ihtimali olaylarda yok.
        IEnumerable<double> probabilities = events.OfType<ObjectiveEvent>().Select(o => o.Probability)
            .Concat(events.OfType<SkirmishEvent>().Select(s => s.Probability))
            .Concat(events.OfType<ThrowEvent>().Select(t => t.Probability))
            .Concat(events.OfType<NexusEvent>().Where(n => n.Probability is not null).Select(n => n.Probability!.Value));
        foreach (var p in probabilities)
        {
            Fights++;
            if (p <= 0.1 + ClampEpsilon || p >= 0.9 - ClampEpsilon) ClampedFights++;
        }

        AddChampions(blue, winner == TeamSide.Blue);
        AddChampions(red, winner == TeamSide.Red);

        // Draft gücü: 5 şampiyonun erken + orta + geç toplamı. Farkı dilimlere ayır, güçlü tarafın kazanma oranına bak.
        var gap = Power(blue) - Power(red);
        if (gap != 0)
        {
            var bucket = Math.Abs(gap) / powerGapBucketSize * powerGapBucketSize;
            var strongerWon = (gap > 0) == (winner == TeamSide.Blue);
            var (games, wins) = ByPowerGap.GetValueOrDefault(bucket);
            ByPowerGap[bucket] = (games + 1, wins + (strongerWon ? 1 : 0));
        }

        // Sinerji farkı puan dökümünde: Synergy kategorisi = sinerji(Mavi) − sinerji(Kırmızı).
        var synergyGap = (int)result.Breakdown[ScoreCategory.Synergy];
        if (synergyGap != 0)
        {
            var betterWon = (synergyGap > 0) == (winner == TeamSide.Blue);
            var (games, wins) = BySynergyGap.GetValueOrDefault(Math.Abs(synergyGap));
            BySynergyGap[Math.Abs(synergyGap)] = (games + 1, wins + (betterWon ? 1 : 0));
        }

        // Comp karakteri: Σ(erken − geç). Fark eşiği aşıyorsa o taraf "belirgin erken comp".
        var styleGap = Style(blue) - Style(red);
        if (Math.Abs(styleGap) >= earlyCompThreshold)
        {
            var early = styleGap > 0 ? TeamSide.Blue : TeamSide.Red;
            EarlyCompMatches++;
            if (winner == early)
            {
                EarlyCompWins++;
                if (result.End.Kind == MatchEndKind.Siege) EarlyCompSiegeWins++;
            }

            var scale = early == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;
            if (Frame(result, 14) is { } f14 && f14.TeamGold(early) - f14.TeamGold(scale) >= CrushedLead)
            {
                CrushedScaleMatches++;
                if (winner == scale) CrushedScaleWins++;
            }
        }
    }

    private void AddEconomy(MatchResult result)
    {
        if (Frame(result, 14) is { } f14)
        {
            // Koridorcular ve ormancı (support CS kesmez): takım sırasında her 5'in son elemanı support.
            foreach (var (c, i) in f14.Champions.Select((c, i) => (c, i)).Where(x => x.i % 5 != 4))
            {
                LanerCsAt14 += c.Cs;
                LanerCsSamples++;
            }
        }

        if (Frame(result, 15) is { } f15)
        {
            TeamLeadAt15 += Math.Abs(f15.TeamGold(TeamSide.Blue) - f15.TeamGold(TeamSide.Red));
            TeamLeadSamples++;
        }

        // Altın farkı → kazanma: o dakikaya ulaşan maçlarda, önde olan (dilimine göre) kazandı mı?
        foreach (var minute in CurveMinutes)
        {
            if (Frame(result, minute) is not { } frame) continue;
            var lead = frame.TeamGold(TeamSide.Blue) - frame.TeamGold(TeamSide.Red);
            if (lead == 0) continue;
            var bucket = LeadBuckets.Last(b => Math.Abs(lead) >= b);
            var leaderWon = (lead > 0) == (result.End.Winner == TeamSide.Blue);
            var (games, wins) = ByGoldLead.GetValueOrDefault((minute, bucket));
            ByGoldLead[(minute, bucket)] = (games + 1, wins + (leaderWon ? 1 : 0));
        }

        // Shutdown (150+ ödül) ve yakalanma (DESIGN.md §16.9; metindeki "yakalandı" ile ayırt edilir).
        foreach (var kill in result.Events.OfType<KillEvent>())
        {
            if (kill.Bounty >= 150)
            {
                Shutdowns++;
                ShutdownGold += kill.Bounty;
            }

            if (kill.Text.Contains("yakalandı"))
            {
                Catches++;
                CatchGold += kill.Bounty;
            }
        }
    }

    // Maç o dakikaya ulaşmadıysa kare yok (son kare maçın bittiği dakikadır, Nexus anı; o da sayılmaz).
    private static GoldFrame? Frame(MatchResult result, int minute) =>
        minute < result.End.Minute ? result.GoldFrames.FirstOrDefault(f => f.Minute == minute) : null;

    private void AddChampions(TeamDraft team, bool won)
    {
        foreach (var c in team.Champions)
        {
            var (_, games, wins) = Champions.GetValueOrDefault(c.Id, (c.Name, 0, 0));
            Champions[c.Id] = (c.Name, games + 1, wins + (won ? 1 : 0));
        }
    }

    private static int Power(TeamDraft team) => team.Champions.Sum(c => c.Early + c.Mid + c.Late);

    private static int Style(TeamDraft team) => team.Champions.Sum(c => c.Early - c.Late);
}
