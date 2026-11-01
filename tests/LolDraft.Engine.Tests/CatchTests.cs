using LolDraft.Engine.Events;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Yakalanma (DESIGN.md §16.9): önde olan takımın en zengini bazen bir yerde yakalanır.</summary>
public sealed class CatchTests
{
    private const int MatchCount = 1000;

    [Theory]
    [InlineData(1000, 0.04)]
    [InlineData(3000, 0.06)]
    [InlineData(9000, 0.12)]
    [InlineData(20000, 0.12)]   // tavan
    public void Yakalanma_ihtimali_takim_farkiyla_artar_ve_sinirlidir(double lead, double expected)
    {
        Assert.Equal(expected, MatchRun.CatchChance(lead), 9);
    }

    // dk ≥ 15, objektifsiz dakikada, 5-40. saniyede; katil geride olan takımdan; kurbanın takımı dakika başında önde.
    [Fact]
    public void Yakalanma_kurala_uyar()
    {
        var simulator = MatchEngine.CreateSimulator();
        var (catches, victimAhead) = (0, 0);
        for (var seed = 0u; seed < MatchCount; seed++)
        {
            foreach (var input in (MatchInput[])[TestTeams.Match(seed), TestTeams.Even(seed)])
            {
                var result = simulator.Simulate(input);
                var objectiveMinutes = result.Events.OfType<ObjectiveEvent>().Select(o => (int)(o.T / 60)).ToHashSet();

                foreach (var kill in result.Events.OfType<KillEvent>().Where(k => k.Text.Contains("yakalandı")))
                {
                    catches++;
                    var minute = (int)(kill.T / 60);
                    Assert.True(minute + 1 >= 15, kill.Text);
                    Assert.DoesNotContain(minute, objectiveMinutes);
                    Assert.InRange(kill.T - minute * 60, 5, 40);
                    Assert.Null(kill.Lane);

                    var victimSide = kill.Side == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;
                    var frame = result.GoldFrames.First(f => f.Minute == minute);
                    if (frame.TeamGold(victimSide) > frame.TeamGold(kill.Side))
                        victimAhead++;
                }
            }
        }

        // Fark yakalanma anında ≥ 1.000; dakika başında da neredeyse her zaman kurbanın takımı önde.
        Assert.True(catches > 300, $"Sadece {catches} yakalanma");
        Assert.True(victimAhead >= 0.95 * catches, $"{victimAhead}/{catches} yakalanmada kurbanın takımı önde");
    }
}
