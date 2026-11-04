using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class DraftSessionTests
{
    [Fact]
    public void Sira_tasarimdaki_turnuva_sirasi()
    {
        // DESIGN.md §2: Ban 1 M K M K M K · Pick 1 M K K M M K · Ban 2 K M K M · Pick 2 K M M K
        var expected = "BM BK BM BK BM BK PM PK PK PM PM PK BK BM BK BM PK PM PM PK";
        var actual = string.Join(' ', DraftOrder.Steps.Select(s =>
            $"{(s.Kind == DraftActionKind.Ban ? 'B' : 'P')}{(s.Side == Side.Blue ? 'M' : 'K')}"));

        Assert.Equal(expected, actual);
        Assert.Equal(20, DraftOrder.Steps.Count);
    }

    [Fact]
    public void Draft_baslarken_mavi_ban_sirasinda_ve_sayac_30_sn()
    {
        var session = Start();

        Assert.Equal(DraftPhase.Ban1, session.Phase);
        Assert.Equal(Side.Blue, session.Turn);
        Assert.Equal(T0 + TimeSpan.FromSeconds(30), session.Deadline);
    }

    [Fact]
    public void Yirmi_adim_bitince_swap_fazi_ve_dizilim_pick_sirasi()
    {
        var session = Start();

        PlaySteps(session, 20);

        Assert.Equal(DraftPhase.Swap, session.Phase);
        Assert.Null(session.Turn);
        Assert.Equal(Picks(session, Side.Blue), session.Lineup(Side.Blue)!.Champions);
        Assert.Equal(Picks(session, Side.Red), session.Lineup(Side.Red)!.Champions);
        Assert.Null(session.Outcome);
    }

    [Fact]
    public void Gecerli_hamle_sayaci_yeniler()
    {
        var session = Start();
        var later = T0 + TimeSpan.FromSeconds(12);

        session.Ban(Side.Blue, Id("c01"), later);

        Assert.Equal(later + TimeSpan.FromSeconds(30), session.Deadline);
    }

    [Fact]
    public void Sirasi_olmayan_hamle_reddedilir_ve_durum_degismez()
    {
        var session = Start();

        var result = session.Ban(Side.Red, Id("c01"), T0);

        Assert.Equal(DraftErrors.NotYourTurn, result.Error);
        Assert.Empty(session.Actions);
        Assert.Equal(Side.Blue, session.Turn);
    }

    [Fact]
    public void Ban_adiminda_pick_reddedilir()
    {
        var session = Start();

        Assert.Equal(DraftErrors.WrongAction, session.Pick(Side.Blue, Id("c01"), T0).Error);
    }

    [Fact]
    public void Havuzda_olmayan_sampiyon_reddedilir()
    {
        var session = Start();

        Assert.Equal(DraftErrors.NotInPool, session.Ban(Side.Blue, Id("teemo"), T0).Error);
    }

    [Fact]
    public void Banli_ya_da_secilmis_sampiyon_tekrar_kullanilamaz()
    {
        var session = Start();
        session.Ban(Side.Blue, Id("c01"), T0);

        Assert.Equal(DraftErrors.ChampionUnavailable, session.Ban(Side.Red, Id("c01"), T0).Error);

        PlaySteps(session, 5);
        session.Pick(Side.Blue, Id("c10"), T0);

        Assert.Equal(DraftErrors.ChampionUnavailable, session.Pick(Side.Red, Id("c10"), T0).Error);
    }

    [Fact]
    public void Sure_dolunca_hamle_reddedilir()
    {
        var session = Start();

        var result = session.Ban(Side.Blue, Id("c01"), T0 + TimeSpan.FromSeconds(30));

        Assert.Equal(DraftErrors.TurnExpired, result.Error);
        Assert.Empty(session.Actions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ahri")]
    [InlineData("lee sin")]
    [InlineData("<script>")]
    [InlineData(null)]
    public void Gecersiz_sampiyon_kimligi_olusmaz(string? value)
    {
        Assert.Equal(DraftErrors.InvalidChampionId, ChampionId.Create(value).Error);
    }

    [Fact]
    public void Havuz_20den_kucukse_draft_baslamaz()
    {
        var result = DraftSession.Start(Pool.Take(19), FearlessLocks.None, DraftTimings.Default, T0);

        Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }
}
