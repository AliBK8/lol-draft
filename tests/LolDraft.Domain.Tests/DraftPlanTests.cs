using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>Oyun planı fazı (DESIGN.md §4.10): swap'tan sonra, iki takım gizlice ve aynı anda plan seçer.</summary>
public sealed class DraftPlanTests
{
    [Fact]
    public void Plan_fazi_swap_kilitlenince_baslar_suresi_20_sn()
    {
        var session = InPlan();

        Assert.Equal(DraftPhase.Plan, session.Phase);
        Assert.Equal(T0 + TimeSpan.FromSeconds(20), session.Deadline);
        Assert.False(session.HasChosenPlan(Side.Blue));
        Assert.False(session.HasChosenPlan(Side.Red));
    }

    [Fact]
    public void Iki_takim_secince_draft_biter_planlar_sonucta()
    {
        var session = InPlan();

        Assert.True(session.ChoosePlan(Side.Red, MatchPlan.Split, T0).IsSuccess);
        Assert.True(session.HasChosenPlan(Side.Red));
        Assert.Equal(DraftPhase.Plan, session.Phase);

        Assert.True(session.ChoosePlan(Side.Blue, MatchPlan.DragonLate, T0).IsSuccess);

        Assert.Equal(DraftPhase.Completed, session.Phase);
        Assert.Equal((MatchPlan.DragonLate, MatchPlan.Split), (session.Outcome!.BluePlan, session.Outcome.RedPlan));
        Assert.Null(session.Deadline);
    }

    [Fact]
    public void Secim_tek_seferlik()
    {
        var session = InPlan();
        session.ChoosePlan(Side.Blue, MatchPlan.Invade, T0);

        Assert.Equal(DraftErrors.PlanChosen, session.ChoosePlan(Side.Blue, MatchPlan.Split, T0).Error);
    }

    [Fact]
    public void Sure_dolunca_secmeyen_Dengeli_oynar()
    {
        var session = InPlan();
        session.ChoosePlan(Side.Blue, MatchPlan.FastFinish, T0);
        var deadline = session.Deadline!.Value;

        Assert.Equal(DraftErrors.TurnExpired, session.ChoosePlan(Side.Red, MatchPlan.Split, deadline).Error);
        Assert.True(session.Timeout(deadline, new FixedRandom(0)).IsSuccess);

        Assert.Equal(DraftPhase.Completed, session.Phase);
        Assert.Equal((MatchPlan.FastFinish, MatchPlan.Balanced), (session.Outcome!.BluePlan, session.Outcome.RedPlan));
    }

    [Fact]
    public void Plan_fazi_disinda_plan_secilemez_plan_fazinda_swap_yapilamaz()
    {
        var session = Start();
        Assert.Equal(DraftErrors.PhaseClosed, session.ChoosePlan(Side.Blue, MatchPlan.Split, T0).Error);

        PlaySteps(session, 20);
        Assert.Equal(DraftErrors.PhaseClosed, session.ChoosePlan(Side.Blue, MatchPlan.Split, T0).Error);

        session.LockLineup(Side.Blue, T0);
        session.LockLineup(Side.Red, T0);
        Assert.Equal(DraftErrors.PhaseClosed, session.SwapRoles(Side.Blue, Role.Top, Role.Mid, T0).Error);

        session.ChoosePlan(Side.Blue, MatchPlan.Balanced, T0);
        session.ChoosePlan(Side.Red, MatchPlan.Balanced, T0);
        Assert.Equal(DraftErrors.PhaseClosed, session.ChoosePlan(Side.Red, MatchPlan.Split, T0).Error);
    }

    [Fact]
    public void Gecersiz_plan_reddedilir()
    {
        var session = InPlan();

        Assert.Equal(DraftErrors.InvalidPlan, session.ChoosePlan(Side.Blue, (MatchPlan)99, T0).Error);
        Assert.False(session.HasChosenPlan(Side.Blue));
    }

    private static DraftSession InPlan()
    {
        var session = Start();
        PlaySteps(session, 20);
        session.LockLineup(Side.Blue, T0);
        session.LockLineup(Side.Red, T0);
        return session;
    }
}
