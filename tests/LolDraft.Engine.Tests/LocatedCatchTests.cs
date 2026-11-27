using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Konumlu yakalanma, ormanda savaş ve arkadan kapatma (DESIGN.md §4.11 D).</summary>
public sealed class LocatedCatchTests
{
    private const int MatchCount = 300;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Yakalanmanin_yeri_kurbanin_yalniz_olup_olmamasina_gore()
    {
        var (lane, jungle, fights, jungleFights) = (0, 0, 0, 0);
        foreach (var (input, events) in Matches())
        {
            foreach (var c in events.OfType<CatchEvent>())
            {
                var preySide = c.Side == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;
                Assert.True((c.Lane is null) != (c.Jungle is null), "yer ya koridor ya orman");
                if (c.Lane is { } l)
                {
                    // Koridorda yakalanan, orada takım arkadaşlarıyla duruyordu.
                    var mates = TestEvents.PostedIn(events, preySide == TeamSide.Blue ? input.Blue : input.Red, preySide, l, c.T);
                    Assert.Contains(mates, m => m.Id != c.VictimId);
                    lane++;
                }
                else
                {
                    // Ormanda: avcının ormanı, o yarının istila kampı.
                    Assert.Equal(c.Side, c.Jungle);
                    Assert.True(c.Camp is JungleCamp.Wolves or JungleCamp.Raptors, $"{c.Camp}");
                    jungle++;
                }

                if (c.HelperIds.Count > 0)
                {
                    fights++;
                    if (c.Jungle is not null)
                        jungleFights++;
                    Assert.NotEqual("", c.Text);
                    Assert.InRange(c.Probability!.Value, 0.1 - 1e-9, 0.9 + 1e-9);
                    // Savaşın kill'leri katılanlar arasında.
                    string[] parts = [.. c.HunterIds, .. c.HelperIds, c.VictimId];
                    foreach (var k in events.OfType<KillEvent>().Where(k => k.T > c.T && k.T <= c.T + 3 && k.Text.Contains("indirdi")))
                    {
                        Assert.Contains(k.KillerId, parts);
                        Assert.Contains(k.VictimId, parts);
                    }
                }
                else
                {
                    Assert.Equal("", c.Text);
                    Assert.Null(c.Probability);
                }
            }
        }

        Assert.True(lane > 0 && jungle > 0, $"koridor {lane}, orman {jungle}");
        Assert.True(jungleFights > 0, $"ormanda savaş yok ({fights} savaş)");
    }

    // Yan koridorda tek başına duran (1-4'ün splitçisi) yakalanırsa rakip ormanında yakalanır.
    [Fact]
    public void Yalniz_yan_koridorcu_rakip_ormaninda_yakalanir()
    {
        var count = 0;
        foreach (var (input, events) in Matches())
        {
            foreach (var c in events.OfType<CatchEvent>().Where(c => !c.Text.Contains("öncesi") && c.T >= 13 * 60))
            {
                var preySide = c.Side == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;
                var team = preySide == TeamSide.Blue ? input.Blue : input.Red;
                var posts = TestEvents.PostsAt(events, preySide, c.T);
                var slot = team.Champions.ToList().FindIndex(x => x.Id == c.VictimId);
                if (posts[slot] is not { } post || posts.Where((p, i) => i != slot && p == post).Any())
                    continue;

                Assert.NotNull(c.Jungle);
                count++;
            }
        }

        Assert.True(count > 0);
    }

    [Fact]
    public void Arkadan_kapatma_14_dakikadan_sonra_ve_cukurun_obur_ucundan()
    {
        var (flanks, openers) = (0, 0);
        foreach (var (input, events) in Matches())
        {
            foreach (var f in events.OfType<FlankEvent>())
            {
                Assert.True(f.T >= 13 * 60, "arkadan kapatma 14. dakikadan önce yok");
                Assert.NotEqual(Objective.FinalFight, f.Objective);
                Assert.InRange(f.FlankerIds.Count, 1, 2);
                var far = f.Objective is Objective.Dragon or Objective.Elder ? Lane.Top : Lane.Bot;
                var team = f.Side == TeamSide.Blue ? input.Blue : input.Red;
                var posted = TestEvents.PostedIn(events, team, f.Side, far, f.T).Select(c => c.Id);
                Assert.All(f.FlankerIds, id => Assert.Contains(id, posted));
                flanks++;

                // Takımı kazandıysa ilk kill arkadan gelenin, savaştan 2 sn sonra.
                var objective = events.OfType<ObjectiveEvent>().Single(o => o.T == f.T);
                var opener = events.OfType<KillEvent>()
                    .FirstOrDefault(k => k.Text.Contains("arkadan dolandı") && f.FlankerIds.Contains(k.KillerId) && Math.Abs(k.T - f.T) < 5);
                if (opener is not null)
                {
                    Assert.Equal(f.Side, objective.Side);
                    Assert.Equal(f.T + 2, opener.T);
                    openers++;
                }
                else if (objective.Side == f.Side)
                {
                    // Kazanan takımın arkadan geleni varsa açılış kill'i ondan (ölmediyse ve kaybedende yaşayan varsa).
                    var opened = events.OfType<KillEvent>().Any(k => k.T == f.T + 2 && k.Side == f.Side);
                    Assert.False(opened, "kazanan arkadan gelenle açılış kill'i başkasına gitti");
                }
            }
        }

        Assert.True(flanks > 0 && openers > 0, $"arkadan gelen {flanks}, açılış kill'i {openers}");
    }

    private IEnumerable<(MatchInput Input, IReadOnlyList<MatchEvent> Events)> Matches()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            foreach (var input in (MatchInput[])[TestTeams.Match(seed), TestTeams.Split(seed)])
                yield return (input, _simulator.Simulate(input).Events);
        }
    }
}
