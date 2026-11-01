using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Kuşatma, throw ve maç sonu kuralları (DESIGN.md §4.6).</summary>
public sealed class SiegeTests
{
    private const int MatchCount = 1000;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Kusatma_20_dakikadan_sonra_objektifsiz_dakikalarin_45_saniyesinde()
    {
        var count = 0;
        foreach (var events in Results().Select(r => r.Events))
        {
            // Kuşatma toplanması: 45. sn'deki SiegeEvent (son savaşın toplanması 55. sn'de).
            var objectiveMinutes = events.OfType<ObjectiveEvent>().Select(o => (int)(o.T / 60) + 1).ToHashSet();
            foreach (var siege in events.OfType<SiegeEvent>().Where(s => s.T % 60 == 45))
            {
                var minute = (int)(siege.T / 60) + 1;
                Assert.True(minute >= 20);
                Assert.DoesNotContain(minute, objectiveMinutes);
                count++;
            }
        }

        Assert.True(count > 0);
    }

    [Fact]
    public void Her_kusatma_ya_Nexus_ya_Throw_ile_sonuclanir_ve_arada_en_az_3_dakika_var()
    {
        foreach (var result in Results())
        {
            var sieges = result.Events.OfType<SiegeEvent>().Where(s => s.T % 60 == 45).ToList();

            foreach (var siege in sieges)
            {
                var outcomes = result.Events.Where(e =>
                    (e is NexusEvent && e.T == siege.T + 8) || (e is ThrowEvent && e.T == siege.T + 6)).ToList();
                Assert.Single(outcomes);
            }

            var minutes = sieges.Select(s => (int)(s.T / 60) + 1).ToList();
            Assert.All(minutes.Zip(minutes.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 3));
        }
    }

    [Fact]
    public void Kusatmayla_biten_macin_sonu_tutarli()
    {
        var sieged = Results().Where(r => r.End.Kind == MatchEndKind.Siege).ToList();

        Assert.NotEmpty(sieged);
        Assert.All(sieged, r =>
        {
            var nexus = Assert.IsType<NexusEvent>(r.Events[^1]);
            Assert.Equal(r.End.Winner, nexus.Side);
            Assert.Equal(r.End.Minute, (int)(nexus.T / 60) + 1);
            Assert.NotNull(nexus.Probability);
            Assert.True(r.End.Minute < 35);
        });
    }

    [Fact]
    public void Nexustan_sonra_olay_yok()
    {
        Assert.All(Results(), r =>
        {
            var nexusT = r.Events.OfType<NexusEvent>().Single().T;
            Assert.All(r.Events, e => Assert.True(e.T <= nexusT));
        });
    }

    // Throw sabit puan yazmaz (eskiden +10); savunan kill'leri ödülleriyle alır.
    [Fact]
    public void Throw_puan_yazmaz_savunan_kill_alir()
    {
        var (throws, defended) = (0, 0);
        foreach (var result in Results())
        {
            Assert.Equal(0, result.Breakdown[ScoreCategory.Siege]);
            foreach (var throwEvent in result.Events.OfType<ThrowEvent>())
            {
                throws++;
                var siegeT = throwEvent.T - 6;
                if (result.Events.OfType<KillEvent>().Any(k =>
                        k.Side == throwEvent.Side && k.T >= siegeT + 2 && k.T <= siegeT + 2 + 1.5 * 2))
                    defended++;
            }
        }

        Assert.True(throws > 0);
        Assert.True(defended >= 0.9 * throws, $"{defended}/{throws} throw'da savunan kill aldı");
    }

    // Kuşatmaya giden: bir koridorda dış + iç kuleyi yıkmış ve dakika başında altında önde (fark kuşatma anında ≥ 2.000).
    [Fact]
    public void Kusatmaya_kapiya_dayanan_ve_altinda_onde_olan_gider()
    {
        var (sieges, ahead) = (0, 0);
        foreach (var result in Results())
        {
            foreach (var siege in result.Events.OfType<SiegeEvent>().Where(s => s.T % 60 == 45))
            {
                sieges++;
                var towers = result.Events.OfType<TowerEvent>().Where(e => e.Side == siege.Side && e.T < siege.T);
                Assert.Contains(towers.GroupBy(e => e.Lane), lane => lane.Count() >= 2);

                var frame = result.GoldFrames.First(f => f.Minute == (int)(siege.T / 60));
                if (frame.TeamGold(siege.Side) > frame.TeamGold(siege.Side.Opponent()))
                    ahead++;
            }
        }

        Assert.True(sieges > 100, $"Sadece {sieges} kuşatma");
        Assert.True(ahead >= 0.95 * sieges, $"{ahead}/{sieges} kuşatmada saldıran dakika başında önde");
    }

    private IEnumerable<MatchResult> Results()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed));
    }
}
