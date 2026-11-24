using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Orman ve gank kuralları (DESIGN.md §4.5 Orman).</summary>
public sealed class JungleTests
{
    private const int MatchCount = 500;
    // Gank pencereleri (dakika, dahil): MatchRun.Jungle.cs ile aynı.
    private static readonly (int From, int To)[] GankWindows = [(4, 4), (6, 7), (9, 10), (12, 13), (17, 18), (23, 24)];

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Gank_pencerelerde_ve_10_55_saniye_arasinda_her_pencerede_en_fazla_bir()
    {
        foreach (var events in Matches())
        {
            var ganks = GankMoves(events).ToList();
            foreach (var g in ganks)
            {
                var minute = (int)(g.T / 60) + 1;
                Assert.Contains(GankWindows, w => minute >= w.From && minute <= w.To);
                Assert.InRange(g.T % 60, 10, 55);
            }

            foreach (var (from, to) in GankWindows)
                Assert.True(ganks.Count(g => (int)(g.T / 60) + 1 >= from && (int)(g.T / 60) + 1 <= to) <= 1);
        }
    }

    [Fact]
    public void Gank_saati_tahmin_edilemez_saniyeler_dagilir()
    {
        var seconds = Matches().SelectMany(GankMoves).Select(g => (int)(g.T % 60)).ToList();

        Assert.NotEmpty(seconds);
        // 10-55 arası 46 tam saniye; 500 maçta en az 40'ı görülmeli ve hiçbiri ganklerin %10'unu geçmemeli.
        Assert.True(seconds.Distinct().Count() >= 40);
        Assert.True(seconds.GroupBy(s => s).Max(g => g.Count()) < seconds.Count * 0.1);
    }

    [Fact]
    public void Ayni_dakikada_objektif_varsa_gank_objektif_savasindan_sonra()
    {
        foreach (var events in Matches())
        {
            var objectives = events.OfType<ObjectiveEvent>().ToList();
            foreach (var g in GankMoves(events))
            {
                var sameMinute = objectives.Where(o => (int)(o.T / 60) == (int)(g.T / 60));
                Assert.All(sameMinute, o => Assert.True(g.T - g.Lead >= o.T + 5, $"gank {g.T}, objektif {o.T}"));
            }
        }
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
