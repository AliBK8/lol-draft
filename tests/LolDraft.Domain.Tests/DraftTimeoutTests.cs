using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class DraftTimeoutTests
{
    private static readonly Instant Expired = T0 + TimeSpan.FromSeconds(30);

    [Fact]
    public void Sure_dolmadan_zaman_asimi_olmaz()
    {
        var session = Start();

        var result = session.Timeout(T0 + TimeSpan.FromSeconds(29), new FixedRandom(0));

        Assert.Equal(DraftErrors.NotExpired, result.Error);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void Ban_adiminda_zaman_asimi_bos_ban()
    {
        var session = Start();

        session.Timeout(Expired, new FixedRandom(0));

        var action = Assert.Single(session.Actions);
        Assert.Null(action.Champion);
        Assert.True(action.ByTimeout);
        Assert.Equal(Side.Red, session.Turn);
        Assert.Equal(Expired + TimeSpan.FromSeconds(30), session.Deadline);
    }

    [Fact]
    public void Pick_adiminda_zaman_asimi_uygun_sampiyonlardan_rastgele_secer()
    {
        var session = Start();
        PlaySteps(session, 6);   // c01-c06 ban'landı

        session.Timeout(Expired, new FixedRandom(0));

        // Sıralı uygun liste c07'den başlar; zar 0 → c07.
        var action = session.Actions[^1];
        Assert.Equal(Id("c07"), action.Champion);
        Assert.Equal(DraftActionKind.Pick, action.Kind);
        Assert.True(action.ByTimeout);
    }

    // §8.46: süre dolunca seçili duran şampiyon (son ön izleme) kilitlenir, LoL'deki gibi.
    [Fact]
    public void Ban_adiminda_zaman_asimi_on_izlenen_sampiyonu_banlar()
    {
        var session = Start();
        session.Preview(Side.Blue, Id("c30"), T0 + TimeSpan.FromSeconds(5));

        session.Timeout(Expired, new FixedRandom(0));

        var action = Assert.Single(session.Actions);
        Assert.Equal(Id("c30"), action.Champion);
        Assert.Equal(DraftActionKind.Ban, action.Kind);
        Assert.True(action.ByTimeout);
    }

    [Fact]
    public void Pick_adiminda_zaman_asimi_on_izlenen_sampiyonu_secer()
    {
        var session = Start();
        PlaySteps(session, 6);
        session.Preview(Side.Blue, Id("c30"), T0);

        session.Timeout(Expired, new FixedRandom(0));

        Assert.Equal(Id("c30"), session.Actions[^1].Champion);
        Assert.Equal(DraftActionKind.Pick, session.Actions[^1].Kind);
    }

    [Fact]
    public void On_izleme_birakilinca_zaman_asimi_eski_kurala_doner()
    {
        var session = Start();
        session.Preview(Side.Blue, Id("c30"), T0);
        session.Preview(Side.Blue, null, T0);   // "Ban yok" ya da seçimi bıraktı

        session.Timeout(Expired, new FixedRandom(0));

        Assert.Null(Assert.Single(session.Actions).Champion);
    }

    [Fact]
    public void Onceki_adimin_on_izlemesi_sonraki_adima_tasinmaz()
    {
        var session = Start();
        session.Preview(Side.Blue, Id("c30"), T0);
        Assert.True(session.Ban(Side.Blue, Id("c01"), T0).IsSuccess);   // mavi başka şampiyonu ban'ladı

        session.Timeout(Expired, new FixedRandom(0));   // kırmızının adımı, ön izlemesi yok

        Assert.Null(session.Actions[^1].Champion);
        Assert.Equal(Side.Red, session.Actions[^1].Side);
    }

    [Fact]
    public void Zaman_asimi_pick_fearless_kilidini_atlar()
    {
        var session = Start(new FearlessLocks([Id("c07"), Id("c08")]));
        PlaySteps(session, 6);

        session.Timeout(Expired, new FixedRandom(0));

        Assert.Equal(Id("c09"), session.Actions[^1].Champion);
    }

    [Fact]
    public void Swap_fazinda_zaman_asimi_o_anki_dizilimle_plan_fazina_gecer()
    {
        var session = Start();
        PlaySteps(session, 20);
        session.SwapRoles(Side.Blue, Role.Top, Role.Bot, T0);

        session.Timeout(Expired, new FixedRandom(0));

        Assert.Equal(DraftPhase.Plan, session.Phase);
        Assert.Equal(Expired + DraftTimings.Default.Plan, session.Deadline);

        session.Timeout(session.Deadline!.Value, new FixedRandom(0));

        Assert.Equal(DraftPhase.Completed, session.Phase);
        Assert.Equal(session.Lineup(Side.Blue), session.Outcome!.Blue);
        Assert.Null(session.Deadline);
    }

    [Fact]
    public void Sadece_zaman_asimlariyla_draft_biter()
    {
        var session = Start();
        var now = T0;

        while (session.Phase != DraftPhase.Completed)
        {
            now = session.Deadline!.Value;
            Assert.True(session.Timeout(now, new FixedRandom(3)).IsSuccess);
        }

        Assert.Equal(5, session.Outcome!.Blue.Champions.Count);
        Assert.Equal(10, session.Outcome.Actions.Count(a => a.Champion is not null));   // 10 pick, ban'lar boş
    }
}
