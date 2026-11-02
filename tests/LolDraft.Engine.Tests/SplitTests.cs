using LolDraft.Engine.Events;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Split (DESIGN.md §16.8 K12): geride olan takımın yan koridor kozu.</summary>
public sealed class SplitTests
{
    private const int MatchCount = 1000;

    [Fact]
    public void Split_ihtimali_split_etiketiyle_artar()
    {
        var red = TestTeams.ScaleRed();
        Assert.Equal(0.4, MatchRun.SplitChance(red.Top), 9);   // Kayle split 2
        var fiora = new ChampionProfile("fiora", "Fiora", AttackRange.Melee, ChampionClass.Fighter, null, 6, 7, 8,
            economy: 2, apShare: 0, new ChampionTags(split: 3, mobility: 2));
        Assert.Equal(0.6, MatchRun.SplitChance(fiora), 9);
    }

    // dk ≥ 14, ejder/Herald/Baron savaşında; splitter top ya da mid, split ≥ 2; cevap veren aynı roldeki rakip.
    // Splitter ve cevap veren objektif savaşında ölmez (orada değiller).
    [Fact]
    public void Split_kurala_uyar_ve_splitter_savasta_yok()
    {
        var (splits, answered) = (0, 0);
        foreach (var (input, events) in Matches())
        {
            foreach (var split in events.OfType<SplitEvent>())
            {
                splits++;
                var fight = events.OfType<ObjectiveEvent>().Single(o => o.T == split.T);
                Assert.True(split.T >= 13 * 60, $"{split.T} sn'de split");
                Assert.Contains(fight.Objective, new[] { Objective.Dragon, Objective.Herald, Objective.Baron });

                var team = split.Side == TeamSide.Blue ? input.Blue : input.Red;
                var rivals = split.Side == TeamSide.Blue ? input.Red : input.Blue;
                var splitter = team.Champions.Single(c => c.Id == split.SplitterId);
                Assert.True(splitter.Tags.Split >= 2);
                Assert.Equal(split.Lane == Lane.Top ? team.Top : team.Mid, splitter);
                if (split.AnswererId is { } answerer)
                {
                    answered++;
                    Assert.Equal(split.Lane == Lane.Top ? rivals.Top.Id : rivals.Mid.Id, answerer);
                }

                var fightKills = events.OfType<KillEvent>().Where(k => k.Lane is null && k.T > split.T && k.T < split.T + 10);
                Assert.All(fightKills, k => Assert.DoesNotContain(k.VictimId, new[] { split.SplitterId, split.AnswererId }));
            }
        }

        Assert.True(splits > 100, $"Sadece {splits} split");
        Assert.InRange(answered, splits / 4, splits * 3 / 4);   // %50 cevap (koridorda değilse cevap yok)
    }

    // Cevap yoksa splitter 10 sn sonra kuleyi alır (koridorda kule kaldıysa). Cevap varsa 6 sn sonra düello:
    // biri ölür; splitter kazandıysa 10. sn'de kule.
    [Fact]
    public void Cevapsiz_split_kule_cevapli_split_duello()
    {
        var (towers, duels) = (0, 0);
        foreach (var (_, events) in Matches())
        {
            foreach (var split in events.OfType<SplitEvent>())
            {
                var tower = events.OfType<TowerEvent>().SingleOrDefault(t => t.T == split.T + 10);
                if (split.AnswererId is null)
                {
                    if (tower is not null)
                    {
                        towers++;
                        Assert.Equal((split.Side, split.Lane), (tower.Side, tower.Lane));
                    }

                    continue;
                }

                var duel = events.OfType<KillEvent>().SingleOrDefault(k => k.T == split.T + 6);
                if (duel is null)
                    continue;
                duels++;
                Assert.Equal(split.Lane, duel.Lane);
                Assert.Equal(new[] { split.SplitterId, split.AnswererId }.Order(), new[] { duel.KillerId, duel.VictimId }.Order());
                // Kule sadece düelloyu splitter kazandıysa (koridorda kule kalmadıysa o da yok).
                if (tower is not null)
                    Assert.Equal((split.SplitterId, split.Side, split.Lane), (duel.KillerId, tower.Side, tower.Lane));
            }
        }

        Assert.True(towers > 0 && duels > 0, $"kule {towers}, düello {duels}");
    }

    [Fact]
    public void Split_etiketi_olmayan_takim_split_yapmaz()
    {
        Assert.All(Enumerable.Range(0, 200), seed =>
            Assert.Empty(MatchEngine.CreateSimulator().Simulate(TestTeams.Even((uint)seed)).Events.OfType<SplitEvent>()));
    }

    private static IEnumerable<(MatchInput Input, IReadOnlyList<MatchEvent> Events)> Matches()
    {
        var simulator = MatchEngine.CreateSimulator();
        for (var seed = 0u; seed < MatchCount; seed++)
        {
            var input = TestTeams.Match(seed);
            yield return (input, simulator.Simulate(input).Events);
        }
    }
}
