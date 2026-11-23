using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>"Ban yok" (§8.33): ban adımını bilerek boş geçmek; sonucu zaman aşımıyla aynı, kaydı farklı.</summary>
public sealed class DraftSkipBanTests
{
    [Fact]
    public void Ban_adiminda_bos_ban_yazilir_sira_ilerler_sayac_yenilenir()
    {
        var session = Start();
        var later = T0 + TimeSpan.FromSeconds(8);

        var result = session.SkipBan(Side.Blue, later);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var action = Assert.Single(session.Actions);
        Assert.Equal(new DraftAction(0, Side.Blue, DraftActionKind.Ban, null, ByTimeout: false), action);
        Assert.Equal(Side.Red, session.Turn);
        Assert.Equal(later + TimeSpan.FromSeconds(30), session.Deadline);
    }

    [Fact]
    public void Pick_adiminda_reddedilir()
    {
        var session = Start();
        PlaySteps(session, 6);   // ilk pick adımı (mavi)

        Assert.Equal(DraftErrors.WrongAction, session.SkipBan(Side.Blue, T0).Error);
        Assert.Equal(6, session.Actions.Count);
    }

    [Fact]
    public void Sirasi_olmayan_ve_suresi_dolan_reddedilir()
    {
        var session = Start();

        Assert.Equal(DraftErrors.NotYourTurn, session.SkipBan(Side.Red, T0).Error);
        Assert.Equal(DraftErrors.TurnExpired, session.SkipBan(Side.Blue, session.Deadline!.Value).Error);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void Hic_ban_atilmasa_da_pick_turu_ancak_alti_ban_adimindan_sonra_gelir()
    {
        var session = Start();
        for (var i = 0; i < 6; i++)
            Assert.True(session.SkipBan(session.Turn!.Value, T0).IsSuccess);

        Assert.Equal(DraftPhase.Pick1, session.Phase);
        Assert.All(session.Actions, a => Assert.Null(a.Champion));
    }
}
