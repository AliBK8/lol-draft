using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class DraftSwapTests
{
    private static DraftSession InSwap()
    {
        var session = Start();
        PlaySteps(session, 20);
        return session;
    }

    [Fact]
    public void Iki_yuva_yer_degistirir()
    {
        var session = InSwap();
        var before = session.Lineup(Side.Blue)!;

        // Arayüzde: top pick'ine tıkla, sonra adc yuvasına tıkla.
        var result = session.SwapRoles(Side.Blue, Role.Top, Role.Bot, T0);

        Assert.True(result.IsSuccess);
        var after = session.Lineup(Side.Blue)!;
        Assert.Equal(before[Role.Top], after[Role.Bot]);
        Assert.Equal(before[Role.Bot], after[Role.Top]);
        Assert.Equal(before[Role.Mid], after[Role.Mid]);
    }

    [Fact]
    public void Swap_rakibin_dizilimine_dokunmaz()
    {
        var session = InSwap();
        var red = session.Lineup(Side.Red)!.Champions.ToArray();

        session.SwapRoles(Side.Blue, Role.Top, Role.Support, T0);

        Assert.Equal(red, session.Lineup(Side.Red)!.Champions);
    }

    [Fact]
    public void Iki_takim_da_kilitleyince_draft_biter()
    {
        var session = InSwap();
        session.SwapRoles(Side.Red, Role.Jungle, Role.Mid, T0);

        session.LockLineup(Side.Blue, T0);
        Assert.Equal(DraftPhase.Swap, session.Phase);

        session.LockLineup(Side.Red, T0);

        Assert.Equal(DraftPhase.Completed, session.Phase);
        Assert.Equal(session.Lineup(Side.Red), session.Outcome!.Red);
        Assert.Equal(20, session.Outcome.Actions.Count);
    }

    [Fact]
    public void Kilitlenen_dizilim_degismez()
    {
        var session = InSwap();
        session.LockLineup(Side.Blue, T0);

        Assert.Equal(DraftErrors.LineupLocked, session.SwapRoles(Side.Blue, Role.Top, Role.Mid, T0).Error);
        Assert.Equal(DraftErrors.LineupLocked, session.LockLineup(Side.Blue, T0).Error);
    }

    [Fact]
    public void Ayni_ya_da_gecersiz_yuva_reddedilir()
    {
        var session = InSwap();

        Assert.Equal(DraftErrors.SameRole, session.SwapRoles(Side.Blue, Role.Mid, Role.Mid, T0).Error);
        Assert.Equal(DraftErrors.InvalidRole, session.SwapRoles(Side.Blue, Role.Mid, (Role)7, T0).Error);
    }

    [Fact]
    public void Swap_fazindan_once_ve_sonra_swap_yapilamaz()
    {
        var session = Start();
        Assert.Equal(DraftErrors.PhaseClosed, session.SwapRoles(Side.Blue, Role.Top, Role.Mid, T0).Error);

        PlaySteps(session, 20);
        session.LockLineup(Side.Blue, T0);
        session.LockLineup(Side.Red, T0);

        Assert.Equal(DraftErrors.PhaseClosed, session.SwapRoles(Side.Red, Role.Top, Role.Mid, T0).Error);
        Assert.Equal(DraftErrors.PhaseClosed, session.Pick(Side.Red, Id("c40"), T0).Error);
    }

    [Fact]
    public void Swap_suresi_dolunca_swap_reddedilir()
    {
        var session = InSwap();

        var result = session.SwapRoles(Side.Blue, Role.Top, Role.Mid, T0 + TimeSpan.FromSeconds(30));

        Assert.Equal(DraftErrors.TurnExpired, result.Error);
    }
}
