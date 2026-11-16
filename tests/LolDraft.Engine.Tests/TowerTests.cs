using LolDraft.Engine.Events;

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
        foreach (var events in Matches())
        {
            var postFight = TestEvents.PostFightTimes(events);
            foreach (var tower in events.OfType<TowerEvent>().Where(t => !postFight.Contains(t.T) && !IsPokePush(t)))
            {
                Assert.True(tower.T >= 13 * 60, "koridor kulesi 14. dakikadan önce düşemez");
                Assert.All(Rivals(tower), rival => Assert.True(TestEvents.IsAbsent(events, rival.Id, tower.T),
                    $"{rival.Name} {tower.T} sn'de koridorda ama kule düştü"));
                count++;
            }
        }

        Assert.True(count > 0);
    }

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

    private IReadOnlyList<ChampionProfile> Rivals(TowerEvent tower) =>
        TestEvents.LaneChampions(tower.Side == TeamSide.Blue ? _red : _blue, tower.Lane);

    private IEnumerable<IReadOnlyList<MatchEvent>> Matches()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed)).Events;
    }
}
