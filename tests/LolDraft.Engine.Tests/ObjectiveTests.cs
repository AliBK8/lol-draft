using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Objektif kuralları (DESIGN.md §4.5 Objektifler): ejder ritmi, ejder ruhu, Elder.</summary>
public sealed class ObjectiveTests
{
    private const int MatchCount = 1000;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Fact]
    public void Herald_Baron_ve_son_savas_sabit_dakikalarda_hepsi_30_saniyede()
    {
        foreach (var objective in Results().SelectMany(r => r.Events.OfType<ObjectiveEvent>()))
        {
            Assert.Equal(30, objective.T % 60);
            Assert.InRange(objective.Probability, 0.1 - 1e-9, 0.9 + 1e-9);
            if (objective.Objective == Objective.Herald) Assert.Equal(14, Minute(objective));
            if (objective.Objective == Objective.Baron) Assert.Equal(23, Minute(objective));
            if (objective.Objective == Objective.FinalFight) Assert.Equal(35, Minute(objective));
        }
    }

    [Fact]
    public void Ejder_6_dakikada_baslar_5_dakika_arayla_gelir_ve_ruhla_durur()
    {
        foreach (var events in Results().Select(r => r.Events))
        {
            var dragons = events.OfType<ObjectiveEvent>().Where(o => o.Objective == Objective.Dragon).ToList();
            var soul = events.OfType<DragonSoulEvent>().SingleOrDefault();

            Assert.Equal(Enumerable.Range(0, dragons.Count).Select(i => 6 + 5 * i), dragons.Select(Minute));
            Assert.InRange(dragons.Count, 1, 7);

            // Ruh: 4. ejderi alan takım; ruhtan sonra ejder yok.
            var blue = 0;
            var red = 0;
            foreach (var dragon in dragons)
            {
                Assert.True(blue < 4 && red < 4, "ruh alındıktan sonra ejder doğmamalı");
                if (dragon.Side == TeamSide.Blue) blue++; else red++;
            }

            if (blue == 4 || red == 4)
            {
                Assert.NotNull(soul);
                Assert.Equal(dragons[^1].T, soul.T);
                Assert.Equal(dragons[^1].Side, soul.Side);
            }
            else
            {
                Assert.Null(soul);
            }
        }
    }

    [Fact]
    public void Elder_sadece_ruhtan_sonra_6_dakika_arayla()
    {
        var elders = 0;
        foreach (var events in Results().Select(r => r.Events))
        {
            var soul = events.OfType<DragonSoulEvent>().SingleOrDefault();
            var elderMinutes = events.OfType<ObjectiveEvent>().Where(o => o.Objective == Objective.Elder).Select(Minute).ToList();
            elders += elderMinutes.Count;

            if (soul is null)
            {
                Assert.Empty(elderMinutes);
                continue;
            }

            // İlk Elder ruhtan 6 dk sonra, sonrakiler 6 dk arayla (dolu dakikaya denk gelirse bir dakika kayar).
            var expected = Minute(soul) + 6;
            foreach (var minute in elderMinutes)
            {
                Assert.InRange(minute, expected, expected + 1);
                expected = minute + 6;
            }
        }

        Assert.True(elders > 0);
    }

    [Theory]
    [InlineData(Objective.Dragon, 2)]
    [InlineData(Objective.Herald, 3)]
    [InlineData(Objective.Baron, 3)]
    [InlineData(Objective.Elder, 3)]
    public void Objektif_savasini_kazanan_en_fazla_max_kill_alir(Objective objective, int maxKills)
    {
        foreach (var events in Results().Select(r => r.Events))
        {
            foreach (var fight in events.OfType<ObjectiveEvent>().Where(o => o.Objective == objective))
            {
                // Savaş kill'leri koridorsuz ve t+2, t+3.5, t+5... saniyelerinde. Kurbanlar arada öldüyse kill düşebilir.
                var kills = events.OfType<KillEvent>()
                    .Where(k => k.Lane is null && k.T >= fight.T + 2 && k.T <= fight.T + 2 + 1.5 * (maxKills - 1)
                                && (k.T - fight.T - 2) % 1.5 == 0)
                    .ToList();

                Assert.InRange(kills.Count, 0, maxKills);
                Assert.All(kills, k => Assert.Equal(fight.Side, k.Side));
            }
        }
    }

    [Fact]
    public void Ejder_puani_geri_donus_ve_ruh_dahil_dokumle_tutarli()
    {
        foreach (var result in Results())
        {
            // Ejder 4 puan, geride olan alırsa 5; ruh +6. Metindeki "geri dönüş" ile puan dökümü tutarlı olmalı.
            var dragons = result.Events.OfType<ObjectiveEvent>()
                .Where(o => o.Objective == Objective.Dragon)
                .Sum(o => o.Side.Sign() * (o.Text.Contains("geri dönüş") ? 5.0 : 4.0));
            var soul = result.Events.OfType<DragonSoulEvent>().Sum(s => s.Side.Sign() * 6.0);

            Assert.Equal(dragons + soul, result.Breakdown[ScoreCategory.Dragon], 9);
        }
    }

    private static int Minute(MatchEvent e) => (int)(e.T / 60) + 1;

    private IEnumerable<MatchResult> Results()
    {
        for (uint seed = 0; seed < MatchCount; seed++)
            yield return _simulator.Simulate(TestTeams.Match(seed));
    }
}
