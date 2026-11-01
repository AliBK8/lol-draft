using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Kuşatma, throw ve maç sonu kuralları (DESIGN.md §4.6).</summary>
public sealed class SiegeTests
{
    private const int MatchCount = 1000;
    private static readonly int[] ObjectiveMinutes = [6, 11, 14, 16, 21, 23, 27, 30, 35];

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Kusatma_20_dakikadan_sonra_objektifsiz_dakikalarin_45_saniyesinde()
    {
        // Kuşatma toplanması: 45. sn'deki SiegeEvent (son savaşın toplanması 55. sn'de).
        var sieges = Results().SelectMany(r => r.Events.OfType<SiegeEvent>()).Where(s => s.T % 60 == 45).ToList();

        Assert.NotEmpty(sieges);
        Assert.All(sieges, s =>
        {
            var minute = (int)(s.T / 60) + 1;
            Assert.True(minute >= 20);
            Assert.DoesNotContain(minute, ObjectiveMinutes);
        });
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

    [Fact]
    public void Throw_savunana_10_puan_yazar()
    {
        foreach (var result in Results())
        {
            var expected = result.Events.OfType<ThrowEvent>().Sum(t => t.Side.Sign() * 10.0);
            Assert.Equal(expected, result.Breakdown[ScoreCategory.Siege]);
        }
    }

    private IEnumerable<MatchResult> Results()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed));
    }
}
