namespace LolDraft.Engine.Tests;

public sealed class DeterminismTests
{
    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Ayni_draft_ve_seed_birebir_ayni_maci_uretir()
    {
        var first = _simulator.Simulate(TestTeams.Match(42));
        var second = _simulator.Simulate(TestTeams.Match(42));

        // Record içindeki liste referansla karşılaştırılır; olayları tek tek (değerle) karşılaştır.
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.End, second.End);
        Assert.Equal(first.Report, second.Report);
        foreach (var category in Enum.GetValues<ScoreCategory>())
            Assert.Equal(first.Breakdown[category], second.Breakdown[category]);
    }

    [Fact]
    public void Farkli_seedler_farkli_maclar_uretir()
    {
        var totals = Enumerable.Range(0, 20)
            .Select(seed => _simulator.Simulate(TestTeams.Match((uint)seed)).Breakdown.Total)
            .ToHashSet();

        Assert.Equal(20, totals.Count);
    }

    [Fact]
    public void Simulator_ornegi_maclar_arasinda_durum_tasimaz()
    {
        var fresh = MatchEngine.CreateSimulator().Simulate(TestTeams.Match(42));

        _simulator.Simulate(TestTeams.Match(1));
        var reused = _simulator.Simulate(TestTeams.Match(42));

        Assert.Equal(fresh.Events, reused.Events);
        Assert.Equal(fresh.Breakdown.Total, reused.Breakdown.Total);
    }
}
