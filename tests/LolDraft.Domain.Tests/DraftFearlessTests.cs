using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

// Tek maç içinde kilitlerin uygulanması; kilitleri seri üretir.
public sealed class DraftFearlessTests
{
    [Fact]
    public void Kendine_kilitli_sampiyon_secilemez()
    {
        var c20 = Id("c20");
        var session = Start(new FearlessLocks([c20], []));
        PlaySteps(session, 6);

        Assert.Equal(DraftErrors.LockedByFearless, session.Pick(Side.Blue, c20, T0).Error);
    }

    [Fact]
    public void Soft_fearless_rakibe_kilitli_sampiyonu_secebilir()
    {
        var c20 = Id("c20");
        var session = Start(new FearlessLocks([], [c20]));
        PlaySteps(session, 6);

        Assert.True(session.Pick(Side.Blue, c20, T0).IsSuccess);
    }

    [Fact]
    public void Iki_tarafa_kilitli_sampiyon_banlanamaz_tek_tarafa_kilitli_banlanabilir()
    {
        var full = Id("c20");
        var soft = Id("c21");
        var session = Start(new FearlessLocks([full, soft], [full]));

        Assert.Equal(DraftErrors.LockedByFearless, session.Ban(Side.Blue, full, T0).Error);
        Assert.True(session.Ban(Side.Blue, soft, T0).IsSuccess);
    }

    [Fact]
    public void Kilitten_sonra_20_sampiyon_kalmazsa_draft_baslamaz()
    {
        // 40'lık havuz, full fearless bo5'in 5. maçı: önceki 4 maçta 40 pick → hiçbir şey kalmaz.
        var locks = new FearlessLocks(Pool.Take(21), Pool.Take(21));

        var result = DraftSession.Start(Pool, locks, DraftTimings.Default, T0);

        Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }

    [Fact]
    public void Kilitler_disaridan_degistirilemez()
    {
        var blue = new List<ChampionId> { Id("c20") };
        var locks = new FearlessLocks(blue, []);

        blue.Add(Id("c21"));

        Assert.DoesNotContain(Id("c21"), locks.Blue);
    }
}
