using LolDraft.Engine.Events;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Kule = fırsat kuralları: koridorda rakip yokken (ölü ya da meşgul) ya da savaş kazanıldıktan sonra.</summary>
public sealed class TowerTests
{
    private const int MatchCount = 1000;
    private const double PostFightDelay = 12;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();
    private readonly TeamDraft _blue = TestTeams.EarlyBlue();
    private readonly TeamDraft _red = TestTeams.ScaleRed();

    [Fact]
    public void Kuleler_sirayla_dis_ic_inhibitor_ve_koridor_basina_en_fazla_3()
    {
        foreach (var events in Matches())
        {
            foreach (var group in events.OfType<TowerEvent>().GroupBy(t => (t.Side, t.Lane)))
                Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(t => t.Tier));
        }
    }

    [Fact]
    public void Savas_sonrasi_kuleyi_objektifi_kazanan_alir_ve_koridor_kurala_uyar()
    {
        var count = 0;
        foreach (var events in Matches())
        {
            var objectives = events.OfType<ObjectiveEvent>().ToDictionary(o => o.T + PostFightDelay);
            foreach (var tower in events.OfType<TowerEvent>().Where(t => objectives.ContainsKey(t.T)))
            {
                var fight = objectives[tower.T];
                Assert.Equal(fight.Side, tower.Side);
                if (fight.Objective == Objective.Dragon)
                    Assert.Equal(Lane.Bot, tower.Lane);
                if (fight.Objective == Objective.Herald)
                    Assert.Equal(Lane.Mid, tower.Lane);
                count++;
            }
        }

        Assert.True(count > 0);
    }

    [Fact]
    public void Koridor_kulesi_14_dakikadan_sonra_ve_rakip_koridorcular_yokken()
    {
        var count = 0;
        var outnumbered = 0;
        foreach (var events in Matches())
        {
            var postFight = TestEvents.PostFightTimes(events);
            foreach (var tower in events.OfType<TowerEvent>().Where(t => !postFight.Contains(t.T) && !IsPokePush(t)))
            {
                Assert.True(tower.T >= 13 * 60, "koridor kulesi 14. dakikadan önce düşemez");
                var present = Rivals(events, tower).Where(r => !TestEvents.IsAbsent(events, r.Id, tower.T)).ToList();
                if (tower.Text.Contains("sayı üstünlüğüyle"))
                {
                    // Kule baskısı (DESIGN.md §4.11 C): savunan var ama bizden müsait olan daha çok.
                    Assert.NotEmpty(present);
                    Assert.True(Attackers(events, tower) > present.Count, $"{tower.Text} @{tower.T}");
                    outnumbered++;
                }
                else
                {
                    Assert.All(present, rival => Assert.Fail($"{rival.Name} {tower.T} sn'de koridorda ama kule düştü"));
                }

                count++;
            }
        }

        Assert.True(count > 0);
        Assert.True(outnumbered > 0, "sayı üstünlüğüyle kule hiç düşmedi");
    }

    // Kule baskısı tablosu (DESIGN.md §4.11 C): boş koridor 1 kişi %20, ek kişi +%15 (en fazla %60); savunan varsa
    // fark başına %10 (en fazla %40); eşitlik/azınlık 0; iç ×0,5, inhibitör ×0,35.
    [Theory]
    [InlineData(1, 0, 0, 0.2)]
    [InlineData(3, 0, 0, 0.5)]
    [InlineData(5, 0, 0, 0.6)]
    [InlineData(5, 1, 0, 0.4)]
    [InlineData(2, 1, 0, 0.1)]
    [InlineData(2, 2, 0, 0)]
    [InlineData(1, 3, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 1, 0.1)]
    [InlineData(5, 1, 2, 0.14)]
    public void Kule_ihtimali_kisi_sayisina_ve_kuleye_gore(int ours, int theirs, int tier, double expected) =>
        Assert.Equal(expected, MatchRun.TowerChance(ours, theirs, tier), 9);

    // K6 poke itişi: fırsat kuralının tek istisnası (rakip koridordayken de düşer). Test takımlarında poke'u fazla
    // olan Kırmızı (scale 4, erken 2): poke'la kuleyi sadece o alır, 14. dakikadan önce değil.
    [Fact]
    public void Poke_itisi_sadece_poke_ustun_tarafa_ve_14_dakikadan_sonra()
    {
        var count = 0;
        foreach (var events in Matches())
        {
            foreach (var tower in events.OfType<TowerEvent>().Where(IsPokePush))
            {
                Assert.Equal(TeamSide.Red, tower.Side);
                Assert.True(tower.T >= 13 * 60, "poke itişi 14. dakikadan önce olamaz");
                count++;
            }
        }

        Assert.True(count > 0);
    }

    private static bool IsPokePush(TowerEvent tower) => tower.Text.Contains("poke'la");

    // Kuleyi alan takımdan düzene göre o koridorda duran ve müsait olanlar. Aynı koridora gank atan (Grup'taki ormancı)
    // koridordan ayrılmış sayılmaz: hareket olayı onu yine bu koridora götürür.
    private int Attackers(IReadOnlyList<MatchEvent> events, TowerEvent tower) =>
        TestEvents.PostedIn(events, tower.Side == TeamSide.Blue ? _blue : _red, tower.Side, tower.Lane, tower.T)
            .Count(c => !TestEvents.IsDead(events, c.Id, tower.T) && !AwayFromLane(events, c.Id, tower.Lane, tower.T));

    private static bool AwayFromLane(IReadOnlyList<MatchEvent> events, string id, Lane lane, double t)
    {
        var sameLaneMoves = events.OfType<MoveEvent>().Where(m => m.Lane == lane).Cast<MatchEvent>().ToHashSet();
        return TestEvents.IsAway([.. events.Where(e => !sameLaneMoves.Contains(e))], id, t);
    }

    // Rakip koridorcular: harita düzenine göre (DESIGN.md §4.11) o an o koridorda duran rakipler.
    private IReadOnlyList<ChampionProfile> Rivals(IReadOnlyList<MatchEvent> events, TowerEvent tower)
    {
        var rivalSide = tower.Side == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;
        return TestEvents.PostedIn(events, rivalSide == TeamSide.Blue ? _blue : _red, rivalSide, tower.Lane, tower.T);
    }

    private IEnumerable<IReadOnlyList<MatchEvent>> Matches()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed)).Events;
    }
}
