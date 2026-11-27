using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>
/// Oyun planı (DESIGN.md §4.10, Tactics.cs). Her planın düğmesi gerçekten çalışıyor mu: aynı draft ve seed'ler
/// planlı ve plansız (Dengeli) oynatılır, planın etkisi ölçülür. Dengeli'nin bit bit aynılığı BalancedPlanFingerprintTests'te.
/// </summary>
public sealed class GamePlanTests
{
    private const int MatchCount = 400;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Theory]
    // EarlyBlue: geç − erken −22, bot erken 8,5, Lee Sin erken 9, top/mid split 1.
    [InlineData(true, GamePlan.EarlyPressure, PlanFit.Good)]
    [InlineData(true, GamePlan.FastFinish, PlanFit.Good)]
    [InlineData(true, GamePlan.BotPressure, PlanFit.Good)]
    [InlineData(true, GamePlan.Invade, PlanFit.Good)]
    [InlineData(true, GamePlan.DragonLate, PlanFit.Poor)]
    [InlineData(true, GamePlan.Split, PlanFit.Neutral)]
    // ScaleRed: geç − erken +23, bot erken 5, Sejuani erken 5, Kayle ve Kassadin split 2, poke 4.
    [InlineData(false, GamePlan.DragonLate, PlanFit.Good)]
    [InlineData(false, GamePlan.Split, PlanFit.Good)]
    [InlineData(false, GamePlan.EarlyPressure, PlanFit.Poor)]
    [InlineData(false, GamePlan.FastFinish, PlanFit.Poor)]
    [InlineData(false, GamePlan.BotPressure, PlanFit.Poor)]
    [InlineData(false, GamePlan.Invade, PlanFit.Poor)]
    [InlineData(false, GamePlan.Balanced, PlanFit.Neutral)]
    public void Uyum_draftin_profilinden(bool earlyTeam, GamePlan plan, PlanFit expected)
    {
        var team = earlyTeam ? TestTeams.EarlyBlue() : TestTeams.ScaleRed();
        Assert.Equal(expected, MatchEngine.PlanFit(team, plan));
    }

    [Fact]
    public void Plan_verilmezse_Dengeli_ile_ayni_mac()
    {
        var input = TestTeams.Match(7);
        var explicitBalanced = new MatchInput(input.Blue, input.Red, input.Seed, GamePlan.Balanced, GamePlan.Balanced);

        Assert.Equal(_simulator.Simulate(input).Events, _simulator.Simulate(explicitBalanced).Events);
    }

    [Fact]
    public void Butun_plan_ciftleriyle_mac_cokmeden_biter_ve_kurallar_gecerli()
    {
        foreach (var blue in Enum.GetValues<GamePlan>())
        {
            foreach (var red in Enum.GetValues<GamePlan>())
            {
                for (uint seed = 0; seed < 20; seed++)
                {
                    var input = TestTeams.Match(seed);
                    var result = _simulator.Simulate(new MatchInput(input.Blue, input.Red, input.Seed, blue, red));
                    Assert.IsType<NexusEvent>(result.Events[^1]);
                    Assert.InRange(result.End.WinnerProbability, 0.1 - 1e-9, 0.9 + 1e-9);
                }
            }
        }
    }

    [Fact]
    public void Erken_baski_ek_gank_atar_dakika_8_ve_11()
    {
        var (planned, balanced) = (0, 0);
        foreach (var (withPlan, without) in Pairs(TestTeams.Even, GamePlan.EarlyPressure))
        {
            planned += Ganks(withPlan).Count(g => (int)(g.T / 60) + 1 is 8 or 11);
            balanced += Ganks(without).Count(g => (int)(g.T / 60) + 1 is 8 or 11);
        }

        Assert.Equal(0, balanced);
        // Nötr uyum: her pencerede %100 → maç başı ~2 (ormancı ölü ya da hedef yoksa düşer).
        Assert.InRange(planned / (double)MatchCount, 1.6, 2.0);
    }

    [Fact]
    public void Erken_baski_geç_oyunda_soner()
    {
        // Eşit draft'ta erken baskının kazanma oranı: plan erken öne geçirir; geç biten maçlarda bedel (güç ×0,96) ödenir.
        var early = Pairs(TestTeams.Even, GamePlan.EarlyPressure).ToList();
        var lateGames = early.Where(p => p.WithPlan.End.Minute >= 30).ToList();
        var earlyGames = early.Where(p => p.WithPlan.End.Minute < 25).ToList();

        Assert.True(WinRate(earlyGames) > WinRate(lateGames), $"erken biten {WinRate(earlyGames):P1}, geç biten {WinRate(lateGames):P1}");
    }

    [Fact]
    public void Bot_baskisi_ganklerin_cogu_bota()
    {
        var mine = Pairs(TestTeams.Even, GamePlan.BotPressure)
            .SelectMany(p => Ganks(p.WithPlan))
            .Where(g => IsBlueJungler(g))
            .ToList();

        Assert.NotEmpty(mine);
        // Nötr uyum: %73 (plansız 1/3).
        Assert.InRange(mine.Count(g => g.Lane == Lane.Bot) / (double)mine.Count, 0.65, 0.81);
    }

    [Fact]
    public void Ejder_ve_late_ejderleri_daha_cok_Heraldi_daha_az_alir()
    {
        double Share(IEnumerable<MatchResult> results, Objective objective)
        {
            var list = results.SelectMany(r => r.Events.OfType<ObjectiveEvent>()).Where(o => o.Objective == objective).ToList();
            return list.Count(o => o.Side == TeamSide.Blue) / (double)list.Count;
        }

        var pairs = Pairs(TestTeams.Even, GamePlan.DragonLate).ToList();
        var dragonWith = Share(pairs.Select(p => p.WithPlan), Objective.Dragon);
        var dragonWithout = Share(pairs.Select(p => p.Without), Objective.Dragon);
        var heraldWith = Share(pairs.Select(p => p.WithPlan), Objective.Herald);
        var heraldWithout = Share(pairs.Select(p => p.Without), Objective.Herald);

        Assert.True(dragonWith > dragonWithout + 0.03, $"ejder {dragonWithout:P1} → {dragonWith:P1}");
        Assert.True(heraldWith < heraldWithout - 0.03, $"Herald {heraldWithout:P1} → {heraldWith:P1}");
    }

    // Split planı sıklığı değil etkinliği artırır (tur 3): rakip daha az cevap verir, split daha çok kule getirir.
    [Fact]
    public void Split_plani_splitin_cevapsiz_kalma_ve_kule_getirme_oranini_artirir()
    {
        static (double Answered, double Towers) Rates(IEnumerable<MatchResult> results)
        {
            // Sadece koridorda hâlâ kule varken yapılan split'ler: harita düzeni (DESIGN.md §4.11) yan koridorun kulesini
            // split'ten önce de düşürebilir; ölçülen split'in kendisinin kule getirmesi.
            var splits = new List<SplitEvent>();
            var towers = 0;
            foreach (var r in results)
            {
                foreach (var s in r.Events.OfType<SplitEvent>().Where(s => s.Side == TeamSide.Blue))
                {
                    if (r.Events.OfType<TowerEvent>().Count(w => w.Side == TeamSide.Blue && w.Lane == s.Lane && w.T < s.T) >= 3)
                        continue;
                    splits.Add(s);
                    towers += r.Events.OfType<TowerEvent>().Count(w => w.Side == TeamSide.Blue && w.Text.Contains("split'te")
                                                                    && w.T > s.T && w.T <= s.T + 10);
                }
            }

            return (splits.Count(s => s.AnswererId is not null) / (double)splits.Count, towers / (double)splits.Count);
        }

        var pairs = Pairs(TestTeams.Split, GamePlan.Split).ToList();
        var with = Rates(pairs.Select(p => p.WithPlan));
        var without = Rates(pairs.Select(p => p.Without));

        Assert.True(with.Answered < without.Answered - 0.08, $"cevap {without.Answered:P1} → {with.Answered:P1}");
        Assert.True(with.Towers > without.Towers + 0.05, $"split başı kule {without.Towers:F2} → {with.Towers:F2}");
    }

    // Kuşatmanın asıl darboğazı kapı (bir koridorda dış + iç kule), plan ona dokunmaz: ilk kuşatma biraz erken
    // (eşik), kuşatma sayısı belirgin fazla (eşik + bekleme 3 → 2 dk).
    [Fact]
    public void Hizli_bitir_kusatmaya_daha_erken_ve_daha_sik_gider()
    {
        // Mavi'nin kuşatma toplanmaları (SiegeEvent). Son savaşla biten maçta son savaştan (dk 35, 30. sn) sonraki
        // SiegeEvent kuşatma değil, Nexus'a yürüyüş: sayılmaz.
        static List<SiegeEvent> Sieges(MatchResult r)
        {
            var finalT = r.End.Kind == MatchEndKind.FinalFight ? (r.End.Minute - 1) * 60 + 30 : double.MaxValue;
            return [.. r.Events.OfType<SiegeEvent>().Where(s => s.Side == TeamSide.Blue && s.T < finalT)];
        }

        var pairs = Pairs(TestTeams.Match, GamePlan.FastFinish).ToList();
        double First(IEnumerable<MatchResult> rs) => rs.Select(Sieges).Where(s => s.Count > 0).Average(s => s[0].T);
        double Count(IEnumerable<MatchResult> rs) => rs.Average(r => Sieges(r).Count);
        var (firstWith, firstWithout) = (First(pairs.Select(p => p.WithPlan)), First(pairs.Select(p => p.Without)));
        var (countWith, countWithout) = (Count(pairs.Select(p => p.WithPlan)), Count(pairs.Select(p => p.Without)));

        Assert.True(firstWith < firstWithout, $"ilk kuşatma {firstWithout / 60:F1} → {firstWith / 60:F1} dk");
        Assert.True(countWith > countWithout * 1.1, $"maç başı kuşatma {countWithout:F2} → {countWith:F2}");
    }

    [Fact]
    public void Istila_plani_istilayi_kesinlestirir_ve_o_takimdan()
    {
        var invades = Pairs(TestTeams.Even, GamePlan.Invade)
            .Select(p => p.WithPlan.Events.OfType<InvadeEvent>().SingleOrDefault())
            .ToList();

        // Ormancı o an ölü ya da meşgulse istila düşer; neredeyse her maçta var.
        Assert.True(invades.Count(i => i is not null) > MatchCount * 0.9);
        Assert.All(invades.Where(i => i is not null), i => Assert.Equal(TeamSide.Blue, i!.Side));
    }

    // Aynı draft ve seed: Mavi planla ve plansız (Kırmızı her zaman Dengeli).
    private IEnumerable<(MatchResult WithPlan, MatchResult Without)> Pairs(Func<uint, MatchInput> make, GamePlan plan)
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var input = make(seed);
            yield return (_simulator.Simulate(new MatchInput(input.Blue, input.Red, input.Seed, plan)), _simulator.Simulate(input));
        }
    }

    private static double WinRate(IReadOnlyCollection<(MatchResult WithPlan, MatchResult Without)> pairs) =>
        pairs.Count(p => p.WithPlan.End.Winner == TeamSide.Blue) / (double)pairs.Count;

    // Gank hareketleri koridorlu MoveEvent'lerdir; ilk katılan ormancı.
    private static IEnumerable<MoveEvent> Ganks(MatchResult result) => result.Events.OfType<MoveEvent>().Where(m => m.Lane is not null);

    private static bool IsBlueJungler(MoveEvent gank) => gank.ParticipantIds[0] == "b1";
}
