using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Nehir çatışması kuralları (DESIGN.md §4.5). Aralıklı dakika/eşitsiz sayı değişikliğinde güncellenecek.</summary>
public sealed class SkirmishTests
{
    private const int MatchCount = 500;
    private static readonly int[] SkirmishMinutes = [8, 13, 18, 26];

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Catisma_belirli_dakikalarin_15_30_saniyeleri_arasinda()
    {
        var skirmishes = Results().SelectMany(r => r.Events.OfType<SkirmishEvent>()).ToList();

        Assert.NotEmpty(skirmishes);
        Assert.All(skirmishes, s =>
        {
            Assert.Contains((int)(s.T / 60) + 1, SkirmishMinutes);
            Assert.InRange(s.T % 60, 15, 30);
            // 1 − 0.9 double'da 0.09999999999999998; sınırda küçük tolerans.
            Assert.InRange(s.Probability, 0.1 - 1e-9, 0.9 + 1e-9);
        });
    }

    [Fact]
    public void Kazanan_katilimcilar_kaybeden_katilimcilardan_1_2_kill_alir()
    {
        var blueIds = TestTeams.EarlyBlue().Champions.Select(c => c.Id).ToHashSet();

        foreach (var events in Results().Select(r => r.Events))
        {
            foreach (var skirmish in events.OfType<SkirmishEvent>())
            {
                var move = events.OfType<MoveEvent>().Single(m => m.T == skirmish.T && m.River is not null);
                var winnerIds = move.ParticipantIds.Where(id => blueIds.Contains(id) == (skirmish.Side == TeamSide.Blue)).ToHashSet();
                var loserIds = move.ParticipantIds.Except(winnerIds).ToHashSet();
                var kills = events.OfType<KillEvent>().Where(k => k.T == skirmish.T + 1 || k.T == skirmish.T + 2.5).ToList();

                Assert.InRange(kills.Count, 1, 2);
                Assert.All(kills, k =>
                {
                    Assert.Contains(k.KillerId, winnerIds);
                    Assert.Contains(k.VictimId, loserIds);
                });
            }
        }
    }

    [Fact]
    public void Catisma_puani_kazanana_3()
    {
        foreach (var result in Results())
        {
            var expected = result.Events.OfType<SkirmishEvent>().Sum(s => s.Side.Sign() * 3.0);
            Assert.Equal(expected, result.Breakdown[ScoreCategory.Skirmish]);
        }
    }

    private IEnumerable<MatchResult> Results()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed));
    }
}
