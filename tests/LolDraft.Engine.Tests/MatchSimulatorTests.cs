using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

public sealed class MatchSimulatorTests
{
    private const int MatchCount = 1000;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Mac_her_zaman_bir_savasla_ve_Nexus_ile_biter()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Match(seed));

            var nexus = Assert.IsType<NexusEvent>(result.Events[^1]);
            Assert.Equal(result.End.Winner, nexus.Side);
            // 1 − 0.9 double'da 0.09999999999999998; sınırda küçük tolerans.
            Assert.InRange(result.End.WinnerProbability, 0.1 - 1e-9, 0.9 + 1e-9);
        }
    }

    [Fact]
    public void Olaylar_zaman_sirasinda()
    {
        var events = _simulator.Simulate(TestTeams.Match(42)).Events;

        Assert.Equal(events.OrderBy(e => e.T), events);
    }

    [Fact]
    public void Iki_taraf_da_kazanabilir()
    {
        var winners = Enumerable.Range(0, MatchCount)
            .Select(seed => _simulator.Simulate(TestTeams.Match((uint)seed)).End.Winner)
            .ToList();

        Assert.Contains(TeamSide.Blue, winners);
        Assert.Contains(TeamSide.Red, winners);
    }

    [Fact]
    public void Toplam_puan_kategorilerin_toplami()
    {
        var breakdown = _simulator.Simulate(TestTeams.Match(42)).Breakdown;

        var sum = Enum.GetValues<ScoreCategory>().Sum(c => breakdown[c]);

        Assert.Equal(sum, breakdown.Total, 9);
    }
}
