using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Nehir çatışması kuralları (DESIGN.md §4.5): dakika aralıkları, iki taraf ayrı ayrı 1-3 kişi.</summary>
public sealed class SkirmishTests
{
    private const int MatchCount = 500;
    private static readonly (int From, int To)[] Windows = [(7, 9), (12, 14), (17, 19), (25, 27)];

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Catisma_araliklarda_her_aralikta_en_fazla_bir_ve_15_30_saniyeleri_arasinda()
    {
        foreach (var result in Results())
        {
            var skirmishes = result.Events.OfType<SkirmishEvent>().ToList();

            Assert.All(skirmishes, s =>
            {
                Assert.Contains(Windows, w => Minute(s) >= w.From && Minute(s) <= w.To);
                Assert.InRange(s.T % 60, 15, 30);
                // 1 − 0.9 double'da 0.09999999999999998; sınırda küçük tolerans.
                Assert.InRange(s.Probability, 0.1 - 1e-9, 0.9 + 1e-9);
            });
            Assert.All(Windows, w => Assert.True(skirmishes.Count(s => Minute(s) >= w.From && Minute(s) <= w.To) <= 1));
        }
    }

    [Fact]
    public void Catisma_dakikalari_aralik_icinde_dagilir_ve_sayilar_esitsiz_olabilir()
    {
        var skirmishes = Results().SelectMany(r => r.Events.OfType<SkirmishEvent>()).ToList();

        // Her aralığın her dakikası kullanılıyor (sabit dakika değil).
        Assert.All(Windows, w => Assert.All(Enumerable.Range(w.From, w.To - w.From + 1),
            m => Assert.Contains(skirmishes, s => Minute(s) == m)));

        Assert.All(skirmishes, s =>
        {
            Assert.InRange(s.BlueCount, 1, 3);
            Assert.InRange(s.RedCount, 1, 3);
            Assert.Contains($"{s.BlueCount}v{s.RedCount}", s.Text);
        });
        Assert.Contains(skirmishes, s => s.BlueCount != s.RedCount);
        Assert.Contains(skirmishes, s => s.BlueCount == s.RedCount);
    }

    private static int Minute(MatchEvent e) => (int)(e.T / 60) + 1;

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
