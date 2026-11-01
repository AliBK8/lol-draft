using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Objektif kuralları (DESIGN.md §4.5 Objektifler). Ejder ruhu/Elder değişikliğinde takvim testi güncellenecek.</summary>
public sealed class ObjectiveTests
{
    private const int MatchCount = 500;

    private static readonly Dictionary<int, Objective> Schedule = new()
    {
        [6] = Objective.Dragon, [11] = Objective.Dragon, [14] = Objective.Herald, [16] = Objective.Dragon,
        [21] = Objective.Dragon, [23] = Objective.Baron, [27] = Objective.Dragon, [30] = Objective.Elder,
        [35] = Objective.FinalFight,
    };

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Objektifler_takvimdeki_dakikanin_30_saniyesinde()
    {
        var objectives = Results().SelectMany(r => r.Events.OfType<ObjectiveEvent>()).ToList();

        Assert.All(objectives, o =>
        {
            Assert.Equal(Schedule[(int)(o.T / 60) + 1], o.Objective);
            Assert.Equal(30, o.T % 60);
            Assert.InRange(o.Probability, 0.1 - 1e-9, 0.9 + 1e-9);
        });
    }

    [Theory]
    [InlineData(Objective.Dragon, 2)]
    [InlineData(Objective.Herald, 3)]
    [InlineData(Objective.Baron, 3)]
    [InlineData(Objective.Elder, 3)]
    public void Objektif_savasini_kazanan_1_ile_max_arasi_kill_alir(Objective objective, int maxKills)
    {
        foreach (var events in Results().Select(r => r.Events))
        {
            foreach (var fight in events.OfType<ObjectiveEvent>().Where(o => o.Objective == objective))
            {
                // Savaş kill'leri koridorsuz ve t+2, t+3.5, t+5... saniyelerinde.
                var kills = events.OfType<KillEvent>()
                    .Where(k => k.Lane is null && k.T >= fight.T + 2 && k.T <= fight.T + 2 + 1.5 * (maxKills - 1)
                                && (k.T - fight.T - 2) % 1.5 == 0)
                    .ToList();

                Assert.InRange(kills.Count, 1, maxKills);
                Assert.All(kills, k => Assert.Equal(fight.Side, k.Side));
            }
        }
    }

    [Fact]
    public void Geri_donus_puani_1_25_kat_ve_log_a_yazilir()
    {
        foreach (var result in Results())
        {
            // Ejder 4 puan; geride olan alırsa 5. Metindeki "geri dönüş" ile puan dökümü tutarlı olmalı.
            var expected = result.Events.OfType<ObjectiveEvent>()
                .Where(o => o.Objective == Objective.Dragon)
                .Sum(o => o.Side.Sign() * (o.Text.Contains("geri dönüş") ? 5.0 : 4.0));

            Assert.Equal(expected, result.Breakdown[ScoreCategory.Dragon], 9);
        }
    }

    private IEnumerable<MatchResult> Results()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed));
    }
}
