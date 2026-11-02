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
        Assert.Equal(expected, MatchRun.CatchChance(lead, opponentPickTotal: 0), 9);
    }

    [Theory]
    [InlineData(3000, 4, 0.096)]    // %6 × (1 + 0,15 × 4)
    [InlineData(9000, 4, 0.192)]    // tavan %12 × 1,6
    public void Rakibin_pick_toplami_yakalanma_ihtimalini_artirir(double lead, int pickTotal, double expected)
    {
        Assert.Equal(expected, MatchRun.CatchChance(lead, pickTotal), 9);
    }

    [Fact]
    public void Hareketli_sampiyon_daha_kolay_kacar()
    {
        Assert.Equal(0.3, MatchRun.EscapeChance(TestTeams.EarlyBlue().Bot), 9);   // Draven mob 1
        Assert.Equal(0.5, MatchRun.EscapeChance(TestTeams.EarlyBlue().Mid), 9);   // LeBlanc mob 3
    }

    // Katil, geride olan takımın müsait oyuncuları içinde en yüksek pick'li. Erken comp önde olunca katil Kırmızı'dan:
    // Sejuani (pick 2) müsaitse o; Kassadin (1) ancak Sejuani yoksa.
    [Fact]
    public void Katil_en_yuksek_pickli_musait_oyuncudur()
    {
        var simulator = MatchEngine.CreateSimulator();
        var killers = new Dictionary<string, int>();
        for (var seed = 0u; seed < MatchCount; seed++)
        {
            var result = simulator.Simulate(TestTeams.Match(seed));
            foreach (var kill in result.Events.OfType<KillEvent>().Where(k => k.Text.Contains("yakalandı") && k.Side == TeamSide.Red))
                killers[kill.KillerId] = killers.GetValueOrDefault(kill.KillerId) + 1;
        }

        Assert.True(killers.GetValueOrDefault("sejuani") > killers.Where(k => k.Key != "sejuani").Sum(k => k.Value),
            string.Join(", ", killers.Select(k => $"{k.Key} {k.Value}")));
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
