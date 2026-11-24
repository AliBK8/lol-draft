using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Ormancı istilası (DESIGN.md §4.5 Orman, MatchRun.Invade.cs).</summary>
public sealed class InvadeTests
{
    private const int MatchCount = 1000;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Maclarin_yaklasik_besinde_birinde_4_05_ile_4_40_arasinda_en_fazla_bir_istila()
    {
        var withInvade = 0;
        foreach (var result in Matches())
        {
            var invades = result.Events.OfType<InvadeEvent>().ToList();
            Assert.True(invades.Count <= 1);
            foreach (var i in invades)
                Assert.InRange(i.T, 4 * 60 + 5, 4 * 60 + 40);
            withInvade += invades.Count;
        }

        // %20 ihtimal; istilacı ormancı o an ölü/meşgulse istila düşer.
        Assert.InRange(withInvade / (double)MatchCount, 0.15, 0.25);
    }

    [Fact]
    public void Cevapsiz_istila_kampi_calar_catisma_yok()
    {
        var uncontested = Invades().Where(x => x.Invade.DefenderIds.Count == 0).ToList();

        Assert.NotEmpty(uncontested);
        Assert.All(uncontested, x =>
        {
            Assert.True(x.Invade.Stolen);
            Assert.Null(x.Invade.Probability);
            Assert.DoesNotContain(x.Events.OfType<KillEvent>(), k => k.T > x.Invade.T && k.T <= x.Invade.T + 4);
        });
    }

    [Fact]
    public void Cevapli_istila_catismadir_kazanan_kaybedenden_bir_iki_kisi_keser()
    {
        var contested = Invades().Where(x => x.Invade.DefenderIds.Count > 0).ToList();

        Assert.NotEmpty(contested);
        Assert.Contains(contested, x => x.Invade.Stolen);
        Assert.Contains(contested, x => !x.Invade.Stolen);
        Assert.All(contested, x =>
        {
            var i = x.Invade;
            Assert.InRange(i.Probability!.Value, 0.1 - 1e-9, 0.9 + 1e-9);
            var (winners, losers) = i.Stolen ? (i.InvaderIds, i.DefenderIds) : (i.DefenderIds, i.InvaderIds);
            var kills = x.Events.OfType<KillEvent>().Where(k => k.T > i.T && k.T <= i.T + 4 && k.Text.Contains("ormanda")).ToList();
            Assert.InRange(kills.Count, 1, Math.Min(2, losers.Count));
            Assert.All(kills, k =>
            {
                Assert.Contains(k.KillerId, winners);
                Assert.Contains(k.VictimId, losers);
            });
        });
    }

    [Fact]
    public void Istilaya_ormanci_gider_yardimci_sadece_mid_ya_da_support()
    {
        var input = TestTeams.Even(0);
        foreach (var (invade, _) in Invades())
        {
            var invaders = invade.Side == TeamSide.Blue ? input.Blue : input.Red;
            var defenders = invade.Side == TeamSide.Blue ? input.Red : input.Blue;
            Assert.Equal(invaders.Jungle.Id, invade.InvaderIds[0]);
            Assert.All(invade.InvaderIds.Skip(1), id => Assert.Contains(id, (string[])[invaders.Mid.Id, invaders.Support.Id]));
            if (invade.DefenderIds.Count > 0)
            {
                Assert.Equal(defenders.Jungle.Id, invade.DefenderIds[0]);
                Assert.All(invade.DefenderIds.Skip(1), id => Assert.Contains(id, (string[])[defenders.Mid.Id, defenders.Support.Id]));
            }
        }
    }

    // Kamp çalınan ormancı geride kalır: kampı alan tarafın ormancısı 8. dakikada rakibinden belirgin fazla CS'de
    // (çalınan 4 CS + 3 dk tempo ≈ 9 CS fark; eşit draft'ta istila olmayan maçlarda fark ~0).
    [Fact]
    public void Kampi_calinan_ormanci_geride_kalir()
    {
        var diffs = new List<double>();
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            var result = _simulator.Simulate(TestTeams.Even(seed));
            if (result.Events.OfType<InvadeEvent>().FirstOrDefault() is not { Stolen: true } invade)
                continue;
            var frame = result.GoldFrames.First(f => f.Minute == 8);
            var blueJungle = frame.Champions[1].Cs;
            var redJungle = frame.Champions[6].Cs;
            diffs.Add(invade.Side == TeamSide.Blue ? blueJungle - redJungle : redJungle - blueJungle);
        }

        Assert.True(diffs.Count > 50, $"{diffs.Count}");
        Assert.True(diffs.Average() > 5, $"ortalama fark {diffs.Average():F1} CS");
    }

    private IEnumerable<MatchResult> Matches()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Even(seed));
    }

    private IEnumerable<(InvadeEvent Invade, IReadOnlyList<MatchEvent> Events)> Invades() =>
        Matches().SelectMany(r => r.Events.OfType<InvadeEvent>().Select(i => (i, r.Events)));
}
