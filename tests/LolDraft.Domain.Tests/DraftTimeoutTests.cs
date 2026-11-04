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

    [Fact]
    public void Zaman_asimi_pick_fearless_kilidini_atlar()
    {
        var session = Start(new FearlessLocks([Id("c07"), Id("c08")], []));
        PlaySteps(session, 6);

        session.Timeout(Expired, new FixedRandom(0));

        Assert.Equal(Id("c09"), session.Actions[^1].Champion);
    }

    [Fact]
    public void Swap_fazinda_zaman_asimi_o_anki_dizilimle_bitirir()
    {
        var session = Start();
        PlaySteps(session, 20);
        session.SwapRoles(Side.Blue, Role.Top, Role.Bot, T0);

        session.Timeout(Expired, new FixedRandom(0));

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
