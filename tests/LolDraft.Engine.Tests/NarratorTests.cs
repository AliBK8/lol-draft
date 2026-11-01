using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Anlatıcı ve sürpriz kuralları (DESIGN.md §4.5, §4.8).</summary>
public sealed class NarratorTests
{
    private const int MatchCount = 500;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Plaka_sadece_5_ile_13_dakikalar_arasinda()
    {
        var plates = AllEvents().OfType<PlateEvent>().ToList();

        Assert.NotEmpty(plates);
        Assert.All(plates, p => Assert.InRange(Minute(p), 5, 13));
    }

    [Fact]
    public void Ayni_koridorda_CS_olaylari_arasinda_en_az_4_dakika_var()
    {
        foreach (var events in Matches())
        {
            foreach (var lane in events.OfType<CsEvent>().GroupBy(c => c.Lane))
            {
                var minutes = lane.Select(Minute).ToList();
                Assert.All(minutes.Zip(minutes.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 4));
            }
        }
    }

    [Fact]
    public void Koridor_basina_dakikada_en_fazla_bir_anlatici_olayi()
    {
        foreach (var events in Matches())
        {
            // Savaş sonrası kule (objektiften 12 sn sonra) anlatıcıdan gelmez; ayrı sayılır.
            var postFight = events.OfType<ObjectiveEvent>().Select(o => o.T + 12).ToHashSet();
            var narrated = events
                .Where(e => e is TowerEvent or PlateEvent or CsEvent && !postFight.Contains(e.T))
                .GroupBy(e => (Lane(e), Minute(e)));

            Assert.All(narrated, g => Assert.Single(g));
        }
    }

    [Fact]
    public void Anlatici_sadece_koridorda_olani_anlatir()
    {
        var blue = TestTeams.EarlyBlue();
        var red = TestTeams.ScaleRed();

        foreach (var events in Matches())
        {
            // CS ve plaka: koridorun yüzü o an koridorda.
            foreach (var e in events.Where(e => e is CsEvent or PlateEvent))
            {
                var (side, lane) = e is CsEvent c ? (c.Side, c.Lane) : (((PlateEvent)e).Side, ((PlateEvent)e).Lane);
                var lead = TestEvents.LaneChampions(side == TeamSide.Blue ? blue : red, lane)[0];
                Assert.False(TestEvents.IsAbsent(events, lead.Id, e.T), $"{lead.Name} {e.T} sn'de koridorda değil: {e.Text}");
            }

            // Solo kill: katil ve kurban koridorda (kurbanın ölümü bu kill'in kendisi, sadece meşguliyete bak).
            foreach (var kill in events.OfType<KillEvent>().Where(k => k.Text.Contains("koridorda öne çıktı")))
            {
                Assert.False(TestEvents.IsAbsent(events, kill.KillerId, kill.T), kill.Text);
                Assert.False(TestEvents.IsAway(events, kill.VictimId, kill.T), kill.Text);
            }
        }
    }

    [Fact]
    public void Ezici_draftta_koridorda_solo_kill_olur()
    {
        var soloKills = AllEvents(TestTeams.Stomp).OfType<KillEvent>().Count(k => k.Lane is not null);

        Assert.True(soloKills > 0);
    }

    [Fact]
    public void Surpriz_3_ile_5_puan_arasi_ve_puan_dokumune_yazilir()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Match(seed));
            var surprises = result.Events.OfType<SurpriseEvent>().ToList();

            Assert.All(surprises, s => Assert.InRange(s.Value, 3, 5));
            Assert.All(surprises, s => Assert.StartsWith("Sürpriz: ", s.Text));
            Assert.Equal(surprises.Sum(s => s.Side.Sign() * s.Value), result.Breakdown[ScoreCategory.Surprise], 9);
        }
    }

    private IEnumerable<IReadOnlyList<MatchEvent>> Matches(Func<uint, MatchInput>? input = null)
    {
        input ??= TestTeams.Match;
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(input(seed)).Events;
    }

    private IEnumerable<MatchEvent> AllEvents(Func<uint, MatchInput>? input = null) => Matches(input).SelectMany(e => e);

    private static int Minute(MatchEvent e) => (int)(e.T / 60) + 1;

    private static Lane Lane(MatchEvent e) => e switch
    {
        TowerEvent t => t.Lane,
        PlateEvent p => p.Lane,
        CsEvent c => c.Lane,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}

internal static class TeamSideTestExtensions
{
    public static int Sign(this TeamSide side) => side == TeamSide.Blue ? 1 : -1;
}
