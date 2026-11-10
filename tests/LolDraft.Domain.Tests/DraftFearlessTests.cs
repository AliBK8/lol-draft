using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

// Tek maç içinde kilitlerin uygulanması; kilitleri seri üretir (SeriesTests).
public sealed class DraftFearlessTests
{
    [Fact]
    public void Kilitli_sampiyon_iki_taraf_icin_de_secilemez()
    {
        var c20 = Id("c20");
        var session = Start(new FearlessLocks([c20]));
        PlaySteps(session, 6);

        // 7. adım mavinin, 8. adım kırmızının pick'i.
        Assert.Equal(DraftErrors.LockedByFearless, session.Pick(Side.Blue, c20, T0).Error);
        PlaySteps(session, 1);
        Assert.Equal(DraftErrors.LockedByFearless, session.Pick(Side.Red, c20, T0).Error);
    }

    [Fact]
    public void Kilitli_sampiyon_banlanamaz()
    {
        var c20 = Id("c20");
        var session = Start(new FearlessLocks([c20]));

        Assert.Equal(DraftErrors.LockedByFearless, session.Ban(Side.Blue, c20, T0).Error);
    }

    [Fact]
    public void Kilitten_sonra_20_sampiyon_kalmazsa_draft_baslamaz()
    {
        // 40'lık havuz, 21 kilit → 19 şampiyon kalır.
        var locks = new FearlessLocks(Pool.Take(21));

        var result = DraftSession.Start(Pool, locks, DraftTimings.Default, T0);

        Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }

    [Fact]
    public void Kilitler_disaridan_degistirilemez()
    {
        var champions = new List<ChampionId> { Id("c20") };
        var locks = new FearlessLocks(champions);

        champions.Add(Id("c21"));

        Assert.False(locks.Contains(Id("c21")));
    }
}
