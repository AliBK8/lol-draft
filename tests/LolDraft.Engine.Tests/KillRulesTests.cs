using LolDraft.Engine.Events;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Ölüm kuralları (DESIGN.md §4.7). Her maçın her kill'i için geçerli olmalı; yeni kurallar eklendikçe de.</summary>
public sealed class KillRulesTests
{
    private const int MatchCount = 1000;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Yeniden_dogma_suresi_oyun_zamaniyla_uzar()
    {
        var deaths = new DeathTracker();
        var champion = TestTeams.EarlyBlue().Top;

        // 10. dk (600 sn): 10 + 600/60 = 20 sn ölü kalır.
        var until = deaths.Kill(champion, 600);

        Assert.Equal(620, until);
        Assert.False(deaths.IsAlive(champion, 619.9));
        Assert.True(deaths.IsAlive(champion, 620));
    }

    private const string TimeOrderIssue =
        "Prototipten gelen dakika içi zaman sırası sorunu; port bitince düzeltilecek (DESIGN.md §14 Bilinen sorunlar 2)";

    [Fact(Skip = TimeOrderIssue)]
    public void Olu_sampiyon_kill_alamaz_ve_tekrar_olemez()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var kills = Kills(seed);
            for (var i = 0; i < kills.Count; i++)
            {
                var kill = kills[i];
                var earlier = kills.Take(i);

                Assert.DoesNotContain(earlier, e => e.VictimId == kill.KillerId && e.T <= kill.T && kill.T < e.Until);
                Assert.DoesNotContain(earlier, e => e.VictimId == kill.VictimId && e.T <= kill.T && kill.T < e.Until);
            }
        }
    }

    [Fact]
    public void Katil_kill_alan_takimdan_kurban_rakipten()
    {
        var blueIds = TestTeams.EarlyBlue().Champions.Select(c => c.Id).ToHashSet();

        for (uint seed = 0; seed < MatchCount; seed++)
        {
            foreach (var kill in Kills(seed))
            {
                Assert.Equal(kill.Side == TeamSide.Blue, blueIds.Contains(kill.KillerId));
                Assert.Equal(kill.Side == TeamSide.Red, blueIds.Contains(kill.VictimId));
            }
        }
    }

    [Fact]
    public void Ilk_kan_macta_tam_bir_kez_yazilir()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var kills = Kills(seed);
            Assert.Single(kills, k => k.Text.StartsWith("İlk kan! "));
            Assert.All(kills, k => Assert.False(k.Text.IndexOf("İlk kan!") > 0));
        }
    }

    [Fact(Skip = TimeOrderIssue)]
    public void Ilk_kan_sadece_ilk_killde_yazilir()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var kills = Kills(seed);
            if (kills.Count == 0)
                continue;

            Assert.StartsWith("İlk kan! ", kills[0].Text);
            Assert.All(kills.Skip(1), k => Assert.DoesNotContain("İlk kan!", k.Text));
        }
    }

    [Fact]
    public void Son_savasi_kazanan_1_ile_4_arasi_kill_alir()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Match(seed));
            // Kuşatmayla erken biten maçta son savaş yok.
            if (result.End.Kind != MatchEndKind.FinalFight)
                continue;
            var final = result.Events.OfType<ObjectiveEvent>().Single(e => e.Objective == Objective.FinalFight);
            // Savaş kill'lerinin koridoru yoktur; dk 35'teki koridor kill'leri sayılmaz.
            var fightKills = result.Events.OfType<KillEvent>().Where(k => k.T > final.T && k.Lane is null).ToList();

            Assert.InRange(fightKills.Count, 1, 4);
            Assert.All(fightKills, k => Assert.Equal(final.Side, k.Side));
        }
    }

    private List<KillEvent> Kills(uint seed) =>
        _simulator.Simulate(TestTeams.Match(seed)).Events.OfType<KillEvent>().ToList();
}
