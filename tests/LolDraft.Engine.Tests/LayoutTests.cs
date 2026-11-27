using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Harita düzeni (DESIGN.md §4.11): motor kimin nerede durduğunu seçer, koridordaki olaylar ona uyar.</summary>
public sealed class LayoutTests
{
    private const int MatchCount = 300;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Duzen_14_dakikadan_once_yok_ve_yerlesim_duzene_uyar()
    {
        var seen = new HashSet<MapLayout>();
        foreach (var (input, events) in Matches(TestTeams.Match))
        {
            foreach (var layout in events.OfType<LayoutEvent>())
            {
                seen.Add(layout.Layout);
                Assert.True(layout.T >= 13 * 60, "düzen 14. dakikadan (13:00) önce seçilmez");
                Assert.Equal("", layout.Text);
                var posts = layout.Posts;
                Assert.Equal(5, posts.Count);
                switch (layout.Layout)
                {
                    case MapLayout.Lanes:
                        Assert.Equal([Lane.Top, null, Lane.Mid, Lane.Bot, Lane.Bot], posts);
                        break;
                    case MapLayout.Group:
                        Assert.All(posts, p => Assert.Equal(posts[0], p));
                        Assert.NotNull(posts[0]);
                        break;
                    case MapLayout.OneFour:
                        Assert.Null(posts[1]);
                        Assert.Equal(1, posts.Count(p => p is Lane.Top or Lane.Bot));
                        Assert.Equal(3, posts.Count(p => p == Lane.Mid));
                        break;
                    case MapLayout.OneThreeOne:
                        Assert.Null(posts[1]);
                        Assert.Equal(1, posts.Count(p => p == Lane.Top));
                        Assert.Equal(1, posts.Count(p => p == Lane.Bot));
                        break;
                }
            }
        }

        // Dört düzen de oynanıyor.
        Assert.Equal(4, seen.Count);
    }

    // Solo kill ve sürpriz kahramanın durduğu koridorda, orada duran rakibe olur.
    [Fact]
    public void Koridor_killerinde_katil_ve_kurban_o_koridorda_durur()
    {
        var count = 0;
        foreach (var (input, events) in Matches(TestTeams.Match))
        {
            foreach (var kill in events.OfType<KillEvent>().Where(k => k.Text.Contains("koridorda öne çıktı") || k.Text.Contains("Sürpriz: ")))
            {
                Assert.Equal(kill.Lane, PostOf(input, events, kill.KillerId, kill.T));
                Assert.Equal(kill.Lane, PostOf(input, events, kill.VictimId, kill.T));
                if (kill.T >= 13 * 60)
                    count++;
            }
        }

        Assert.True(count > 0, "14. dakikadan sonra koridor kill'i yok");
    }

    // Gank, düzene göre o koridorda duran birine atılır.
    [Fact]
    public void Gank_kurbani_o_koridorda_durur()
    {
        var count = 0;
        foreach (var (input, events) in Matches(TestTeams.Match))
        {
            foreach (var kill in events.OfType<KillEvent>().Where(k => k.Text.Contains("gank attı") || k.Text.Contains("baskın yaptı")))
            {
                Assert.Equal(kill.Lane, PostOf(input, events, kill.VictimId, kill.T));
                if (kill.T >= 13 * 60)
                    count++;
            }
        }

        Assert.True(count > 0, "14. dakikadan sonra gank kill'i yok");
    }

    // Kuşatma bir koridordan girer: o koridorda saldıranın dış ve iç kulesi yıkılmış (kapı açık).
    [Fact]
    public void Kusatma_kapisi_acik_koridordan_girer()
    {
        var count = 0;
        foreach (var (_, events) in Matches(TestTeams.Stomp))
        {
            foreach (var siege in events.OfType<SiegeEvent>().Where(s => s.Lane is not null))
            {
                var destroyed = events.OfType<TowerEvent>().Count(w => w.Side == siege.Side && w.Lane == siege.Lane && w.T <= siege.T);
                Assert.True(destroyed >= 2, $"{siege.Lane} koridorunda {destroyed} kule yıkılmışken kuşatma");
                count++;
            }
        }

        Assert.True(count > 0);
    }

    // Plan eğilimi: Split planı yan koridorlu düzenleri (1-4, 1-3-1), Hızlı bitir Grup'u sıklaştırır.
    [Theory]
    [InlineData(GamePlan.Split, new[] { MapLayout.OneFour, MapLayout.OneThreeOne })]
    [InlineData(GamePlan.FastFinish, new[] { MapLayout.Group })]
    public void Plan_duzen_egilimini_kaydirir(GamePlan plan, MapLayout[] favored)
    {
        double Share(GamePlan bluePlan)
        {
            var layouts = Enumerable.Range(0, MatchCount)
                .SelectMany(seed =>
                {
                    var input = TestTeams.Match((uint)seed);
                    var withPlan = new MatchInput(input.Blue, input.Red, input.Seed, bluePlan);
                    return _simulator.Simulate(withPlan).Events.OfType<LayoutEvent>().Where(l => l.Side == TeamSide.Blue);
                })
                .ToList();
            return layouts.Count(l => favored.Contains(l.Layout)) / (double)layouts.Count;
        }

        var without = Share(GamePlan.Balanced);
        var with = Share(plan);
        Assert.True(with > without + 0.08, $"{string.Join("/", favored)} payı {without:P1} → {with:P1}");
    }

    private IEnumerable<(MatchInput Input, IReadOnlyList<MatchEvent> Events)> Matches(Func<uint, MatchInput> input)
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var match = input(seed);
            yield return (match, _simulator.Simulate(match).Events);
        }
    }

    // Şampiyonun t anındaki düzen koridoru (olaydan hemen önce: aynı saniyede yeni düzen seçilmez, 1. saniye hariç).
    private static Lane? PostOf(MatchInput input, IReadOnlyList<MatchEvent> events, string id, double t)
    {
        var blueSlot = input.Blue.Champions.ToList().FindIndex(c => c.Id == id);
        var (side, slot) = blueSlot >= 0
            ? (TeamSide.Blue, blueSlot)
            : (TeamSide.Red, input.Red.Champions.ToList().FindIndex(c => c.Id == id));
        return TestEvents.PostsAt(events, side, t)[slot];
    }
}
