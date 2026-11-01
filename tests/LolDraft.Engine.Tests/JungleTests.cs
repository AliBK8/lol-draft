using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Orman ve gank kuralları (DESIGN.md §4.5 Orman).</summary>
public sealed class JungleTests
{
    private const int MatchCount = 500;
    private static readonly int[] GankMinutes = [3, 6, 9, 12, 18, 24];

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Gank_sadece_belirli_dakikalarin_40_saniyesinde()
    {
        var ganks = Matches().SelectMany(GankMoves).ToList();

        Assert.NotEmpty(ganks);
        Assert.All(ganks, g =>
        {
            Assert.Contains((int)(g.T / 60) + 1, GankMinutes);
            Assert.Equal(40, g.T % 60);
        });
    }

    [Fact]
    public void Her_gankin_tek_sonucu_var_ve_killi_ormanci_alir()
    {
        foreach (var events in Matches())
        {
            foreach (var gank in GankMoves(events))
            {
                var outcomes = events.Where(e => e.T == gank.T && e is KillEvent or GankFailEvent).ToList();
                var outcome = Assert.Single(outcomes);

                if (outcome is KillEvent kill)
                {
                    Assert.Equal(gank.ParticipantIds[0], kill.KillerId);
                    Assert.Equal(gank.Lane, kill.Lane);
                }
            }
        }
    }

    [Fact]
    public void Support_sadece_lane_fazinda_ve_bot_disina_katilir()
    {
        var withSupport = Matches().SelectMany(GankMoves).Where(g => g.ParticipantIds.Count == 2).ToList();

        Assert.NotEmpty(withSupport);
        Assert.All(withSupport, g =>
        {
            Assert.True(g.T < 13 * 60, "support 14. dk'dan sonra katılamaz");
            Assert.NotEqual(Lane.Bot, g.Lane);
        });
    }

    [Fact]
    public void Ayni_icerikli_hareket_olaylari_esittir()
    {
        var a = new MoveEvent(100, "", ["leesin", "leona"], Lane.Top, null, 14, 4);
        var b = new MoveEvent(100, "", ["leesin", "leona"], Lane.Top, null, 14, 4);

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { ParticipantIds = ["leesin"] });
    }

    private IEnumerable<IReadOnlyList<MatchEvent>> Matches()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed)).Events;
    }

    // Gank hareketleri koridorlu MoveEvent'lerdir (nehir çatışması hareketleri konumludur).
    private static IEnumerable<MoveEvent> GankMoves(IReadOnlyList<MatchEvent> events) =>
        events.OfType<MoveEvent>().Where(m => m.Lane is not null);
}
