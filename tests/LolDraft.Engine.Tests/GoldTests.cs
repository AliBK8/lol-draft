using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Altın defteri ve dakika kareleri (DESIGN.md §16.4, §16.11).</summary>
public sealed class GoldTests
{
    private const int MatchCount = 300;

    private static readonly ChampionProfile Kayle = TestTeams.ScaleRed().Top;

    [Fact]
    public void Defter_yasarken_saniyede_2_altin_pasif_gelir_verir()
    {
        var ledger = new GoldLedger();

        Assert.Equal(500, ledger.GoldAt(Kayle, 0));
        Assert.Equal(500 + 2 * 600, ledger.GoldAt(Kayle, 600));
    }

    [Fact]
    public void Olu_gelir_almaz_ve_olum_suresi_sorulan_anda_kirpilir()
    {
        var ledger = new GoldLedger();
        ledger.Died(Kayle, 100, 120);

        Assert.Equal(500 + 2 * 100, ledger.GoldAt(Kayle, 110)); // hâlâ ölü: 100'den sonrası yok
        Assert.Equal(500 + 2 * 180, ledger.GoldAt(Kayle, 200)); // 20 sn ölü kaldı
    }

    [Fact]
    public void Kazanilan_altin_kaynagina_gore_tutulur()
    {
        var ledger = new GoldLedger();
        ledger.Earn(Kayle, GoldSource.Kill, 300);
        ledger.Earn(Kayle, GoldSource.Plate, 160);

        Assert.Equal(300, ledger.Earned(Kayle, GoldSource.Kill));
        Assert.Equal(0, ledger.Earned(Kayle, GoldSource.Tower));
        Assert.Equal(500 + 460, ledger.GoldAt(Kayle, 0));
    }

    [Fact]
    public void Kareler_0_dakikadan_mac_sonuna_her_dakika_on_sampiyonla()
    {
        foreach (var result in Results())
        {
            Assert.Equal(Enumerable.Range(0, result.End.Minute + 1), result.GoldFrames.Select(f => f.Minute));
            Assert.All(result.GoldFrames, f => Assert.Equal(10, f.Champions.Count));
            Assert.All(result.GoldFrames[0].Champions, c => Assert.Equal(500, c.Gold));
            // 1. dakikada olay yok (ilk gank 3. dk): support 500 + 2 × 60 + 90 (support item), CS 0.
            var supports = result.GoldFrames[1].Champions.Where((_, i) => i % 5 == 4).ToList();
            Assert.All(supports, c => Assert.Equal((710, 0), (c.Gold, c.Cs)));
        }
    }

    [Theory]
    [InlineData(0, 1.4, 1.4)]          // eşit koridor: ikisi de normal tempo
    [InlineData(0.7, 1.5225, 1.155)]   // kaçan 1,4 × 0,175 = 0,245; yarısını (0,1225) ezen keser
    [InlineData(-0.7, 1.155, 1.5225)]  // Kırmızı ezerse ayna
    [InlineData(5, 1.75, 0.7)]         // kaçan oran en fazla %50
    public void Ezilen_cs_kacirir_kacanin_yarisini_ezen_keser(double delta, double blue, double red)
    {
        var (blueCs, redCs) = MatchRun.SplitCs(1.4, delta, bluePresent: true, redPresent: true);

        Assert.Equal(blue, blueCs, 9);
        Assert.Equal(red, redCs, 9);
    }

    [Fact]
    public void Koridorda_olmayan_cs_kesemez()
    {
        var (blueAbsent, redHere) = MatchRun.SplitCs(1.4, 0.7, bluePresent: false, redPresent: true);
        var (blueHere, redAbsent) = MatchRun.SplitCs(1.4, 0.7, bluePresent: true, redPresent: false);

        Assert.Equal(0, blueAbsent);
        Assert.Equal(1.155, redHere, 9);
        Assert.Equal(1.5225, blueHere, 9);
        Assert.Equal(0, redAbsent);
    }

    [Theory]
    [InlineData(5000, 5000, 600, 0)]     // başa baş: ödül yok, ne kadar yaşarsa yaşasın
    [InlineData(4000, 5000, 600, 0)]     // geride olanın ödülü yok
    [InlineData(6000, 5000, 0, 300)]     // 1.000 önde, yeni doğmuş → 0,3 × 1.000
    [InlineData(6000, 5000, 300, 495)]   // 5 dk ölmedi → 300 × (1 + 5 × 0,13)
    [InlineData(5050, 5000, 600, 34)]    // 50 önde, 10 dk: 15 × 2,3 — küçük fark küçük kalır
    [InlineData(7000, 5000, 600, 700)]   // 600 × 2,3 → tavan 700
    public void Odul_onde_olmaya_ve_olmeden_gecen_sureye_gore(double victim, double counterpart, double alive, int expected)
    {
        Assert.Equal(expected, MatchRun.Bounty(victim, counterpart, alive));
    }

    // Ödül defterde katile yazılır, olaydaki miktarla aynıdır; büyük ödül "Shutdown!" diye anlatılır.
    [Fact]
    public void Odul_olayla_tutarli_ve_buyugu_shutdown_olarak_anlatilir()
    {
        var shutdowns = 0;
        foreach (var (input, run, result) in Runs())
        {
            var kills = result.Events.OfType<KillEvent>().ToList();
            foreach (var champion in input.Blue.Champions.Concat(input.Red.Champions))
            {
                var expected = kills.Where(k => k.KillerId == champion.Id).Sum(k => k.Bounty);
                Assert.Equal(expected, run.Gold.Earned(champion, GoldSource.Bounty), 6);
            }

            Assert.All(kills, k => Assert.InRange(k.Bounty, 0, 700));
            Assert.All(kills, k => Assert.Equal(k.Bounty >= 150, k.Text.Contains("Shutdown!")));
            shutdowns += kills.Count(k => k.Bounty >= 150);
        }

        Assert.True(shutdowns > 100, $"Sadece {shutdowns} shutdown");
    }

    // Bir dakikada kesilen CS ≤ yaşanan dilim sayısı × dilim başı en fazla (1,4 × 1,25). Dilimler 5, 15, ..., 55. sn.
    [Fact]
    public void Olu_cs_kesemez()
    {
        foreach (var (_, _, result) in Runs())
        {
            var kills = result.Events.OfType<KillEvent>().ToList();
            for (var m = 1; m < result.GoldFrames.Count - 1; m++)
            {
                for (var i = 0; i < 10; i++)
                {
                    var id = result.GoldFrames[m].Champions[i].ChampionId;
                    var alive = Enumerable.Range(0, 6).Select(k => (m - 1) * 60 + 5 + k * 10.0)
                        .Count(t => !kills.Any(x => x.VictimId == id && x.T <= t && t < x.Until));
                    var gained = result.GoldFrames[m].Champions[i].Cs - result.GoldFrames[m - 1].Champions[i].Cs;

                    Assert.True(gained <= alive * 1.75 + 1, $"{id} {m}. dk: {gained} CS, {alive} dilim yaşadı");
                }
            }
        }
    }

    // Ezici draft'ta Mavi koridorcular her maçta daha çok CS keser; support hiç kesmez.
    [Fact]
    public void Ezilen_koridorcu_daha_az_cs_ve_farm_altini_alir()
    {
        for (var seed = 0u; seed < MatchCount; seed++)
        {
            var input = TestTeams.Stomp(seed);
            var run = new MatchRun(input, new Mulberry32(input.Seed));
            var frame = run.Play().GoldFrames.First(f => f.Minute == 14);
            var blue = frame.Champions.Take(5).ToList();
            var red = frame.Champions.Skip(5).ToList();

            foreach (var i in (int[])[0, 1, 2, 3])
                Assert.True(blue[i].Cs > red[i].Cs, $"seed {seed}, rol {i}: {blue[i].Cs} ≤ {red[i].Cs}");
            Assert.Equal((0, 0), (blue[4].Cs, red[4].Cs));
            Assert.True(frame.TeamGold(TeamSide.Blue) > frame.TeamGold(TeamSide.Red));
        }
    }

    // Defter olaylarla tutarlı: her kaynak, olay listesindeki karşılığının altını kadar.
    [Fact]
    public void Kaynaklar_olaylarla_tutarli()
    {
        foreach (var (input, run, result) in Runs())
        {
            foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
            {
                var team = side == TeamSide.Blue ? input.Blue : input.Red;
                double Sum(GoldSource source) => team.Champions.Sum(c => run.Gold.Earned(c, source));
                var events = result.Events;
                var kills = events.OfType<KillEvent>().Count(k => k.Side == side);

                Assert.Equal(300.0 * kills, Sum(GoldSource.Kill), 6);
                Assert.Equal(160.0 * events.OfType<PlateEvent>().Count(p => p.Side == side), Sum(GoldSource.Plate), 6);
                // Kule başına 250 koridorculara + kalanlara 50'şer: bot'ta 250 + 3 × 50 = 400, diğerlerinde 250 + 4 × 50 = 450.
                var towerGold = events.OfType<TowerEvent>().Where(e => e.Side == side)
                    .Sum(e => e.Lane == Lane.Bot ? 400.0 : 450.0);
                Assert.Equal(towerGold, Sum(GoldSource.Tower), 6);
                var objectiveGold = events.OfType<ObjectiveEvent>().Where(o => o.Side == side)
                    .Sum(o => 5 * PerChampion(o.Objective));
                Assert.Equal(objectiveGold, Sum(GoldSource.Objective), 6);
                // Kill başına asist en fazla 150 (solo kill'de 0).
                Assert.InRange(Sum(GoldSource.Assist), 0, 150.0 * kills + 1e-6);
                // Farm altını = CS × 20; support item sadece support'a.
                Assert.All(team.Champions, c => Assert.Equal(20 * run.Gold.Cs(c), run.Gold.Earned(c, GoldSource.Farm), 6));
                Assert.All(team.Champions.Take(4), c => Assert.Equal(0, run.Gold.Earned(c, GoldSource.SupportItem)));
            }
        }
    }

    // Son kare = 500 + 2 × (Nexus anına kadar yaşanan süre) + kazanılan. Ölüm süreleri KillEvent'lerden.
    [Fact]
    public void Son_kare_olumleri_dusulmus_pasif_gelirle_tutarli()
    {
        foreach (var (input, run, result) in Runs())
        {
            var nexusT = result.Events.OfType<NexusEvent>().Single().T;
            var last = result.GoldFrames[^1];

            foreach (var champion in input.Blue.Champions.Concat(input.Red.Champions))
            {
                var dead = result.Events.OfType<KillEvent>().Where(k => k.VictimId == champion.Id)
                    .Sum(k => Math.Min(k.Until, nexusT) - k.T);
                var earned = Enum.GetValues<GoldSource>().Sum(s => run.Gold.Earned(champion, s));
                var expected = 500 + 2 * (nexusT - dead) + earned;

                Assert.InRange(last.Champions.Single(c => c.ChampionId == champion.Id).Gold, expected - 1, expected);
            }
        }
    }

    [Fact]
    public void Altin_mac_akisini_degistirmez_ayni_seed_ayni_kareler()
    {
        var simulator = MatchEngine.CreateSimulator();

        Assert.Equal(simulator.Simulate(TestTeams.Match(42)).GoldFrames, simulator.Simulate(TestTeams.Match(42)).GoldFrames);
    }

    private static double PerChampion(Objective objective) => objective switch
    {
        Objective.Dragon => 40,
        Objective.Herald => 100,
        Objective.Baron => 300,
        _ => 0,
    };

    private static IEnumerable<MatchResult> Results() => Runs().Select(r => r.Result);

    // Hem dengeli (erken vs scale) hem ezici maçlar: ezicide kule, plaka ve kill bol.
    private static IEnumerable<(MatchInput Input, MatchRun Run, MatchResult Result)> Runs()
    {
        for (var seed = 0u; seed < MatchCount; seed++)
        {
            foreach (var input in (MatchInput[])[TestTeams.Match(seed), TestTeams.Stomp(seed)])
            {
                var run = new MatchRun(input, new Mulberry32(input.Seed));
                yield return (input, run, run.Play());
            }
        }
    }
}
