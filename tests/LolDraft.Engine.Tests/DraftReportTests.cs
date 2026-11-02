using LolDraft.Engine.Events;
using static LolDraft.Engine.AttackRange;
using static LolDraft.Engine.ChampionClass;

namespace LolDraft.Engine.Tests;

/// <summary>Draft karnesi (DESIGN.md §16.11): etkili kurallar, taşıyıcılar, altın kaynakları.</summary>
public sealed class DraftReportTests
{
    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    // Erken comp'un engage'i her objektif ve son savaşta işler (K5 Mavi +2); karnede savaş sayısı kadar, Mavi lehine.
    [Fact]
    public void Engage_her_objektif_savasinda_sayilir_ve_mavinin_lehine()
    {
        for (var seed = 0u; seed < 50; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Match(seed));
            var fights = result.Events.OfType<ObjectiveEvent>().Count();
            var engage = result.Report.Rules
                .Where(r => r.Rule == "K5" && r.Context is RuleContext.Objective or RuleContext.FinalFight).ToList();

            Assert.Equal(fights, engage.Sum(r => r.Count));
            Assert.All(engage, r => Assert.Equal((TeamSide.Blue, "leona"), (r.Side, r.ChampionId)));
            Assert.All(engage, r => Assert.InRange(r.Impact, 0.5, 10));   // d +2 → kazanma şansı birkaç puan
        }
    }

    // Koridor kuralının etkisi altın: eşit draft'ta Mavi üst range (karşısı mesafe kapatamayan melee) → K1 Mavi lehine
    // 13 dakika, farm altını farkına pozitif katkı.
    [Fact]
    public void Range_ustunlugu_koridorda_altin_olarak_raporlanir()
    {
        var total = 0.0;
        for (var seed = 0u; seed < 200; seed++)
        {
            var even = TestTeams.Even(seed);
            var blue = new TeamDraft(Champ("b0", Ranged), even.Blue.Jungle, even.Blue.Mid, even.Blue.Bot, even.Blue.Support);
            var red = new TeamDraft(Champ("r0", Melee), even.Red.Jungle, even.Red.Mid, even.Red.Bot, even.Red.Support);
            var range = Assert.Single(_simulator.Simulate(new MatchInput(blue, red, even.Seed)).Report.Rules, r => r.Rule == "K1");

            Assert.Equal((TeamSide.Blue, RuleContext.TopLane, 13, "Range üstünlüğü (b0)"), (range.Side, range.Context, range.Count, range.Text));
            total += range.Impact;
        }

        // Dilim başı ~0,16 CS × 20 altın, 13 dk × 6 dilim → ~250 altın (koridorda olmadığı dilimler hariç).
        Assert.InRange(total / 200, 100, 400);
    }

    [Fact]
    public void Hiper_tasiyicilar_raporlanir()
    {
        var result = _simulator.Simulate(TestTeams.Match(7));

        Assert.Equal(new[] { "kayle", "kassadin", "jinx" }, result.Report.Carries.Select(c => c.ChampionId));
        Assert.All(result.Report.Carries, c =>
        {
            Assert.Equal((TeamSide.Red, result.End.Minute), (c.Side, c.Minute));
            Assert.InRange(c.PotentialShare, 0.05, 1.4);
            Assert.Contains(c.Minute + ". dk", c.Text);
        });
    }

    // Kaynakların toplamı son karedeki takım altınına eşit (başlangıç 2.500 hariç; karede kişi başı aşağı yuvarlama).
    [Fact]
    public void Altin_kaynaklari_takim_altinini_aciklar()
    {
        for (var seed = 0u; seed < 50; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Match(seed));
            var last = result.GoldFrames[^1];
            foreach (var report in (TeamGoldReport[])[result.Report.Blue, result.Report.Red])
            {
                Assert.InRange(report.Total + 2500 - last.TeamGold(report.Side), -1, 6);
                Assert.True(report.Passive > 0 && report.Farm > 0 && report.SupportItem > 0);
            }
        }
    }

    private static ChampionProfile Champ(string id, AttackRange range) =>
        new(id, id, range, Fighter, null, 6, 6, 6, economy: 2, apShare: 0, new ChampionTags(frontline: 2));
}
