using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>
/// Ön izleme (§8.32): hamleyle aynı kurallardan geçer ama hiçbir şeyi değiştirmez; geçerliyse adımın numarası döner.
/// </summary>
public sealed class DraftPreviewTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);

    [Fact]
    public void Sirasi_gelen_takim_ban_adiminda_on_izler_adim_numarasi_doner()
    {
        var session = Start();

        var result = session.Preview(Side.Blue, Id("c05"), T0);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void Pick_adiminda_da_on_izlenir_adim_numarasi_ilerler()
    {
        var session = Start();
        PlaySteps(session, 6);   // 6 ban bitti, 7. adım (numara 6) mavinin ilk pick'i

        var result = session.Preview(Side.Blue, Id("c30"), T0);

        Assert.Equal(6, result.Value);
    }

    [Fact]
    public void On_izleme_durumu_degistirmez()
    {
        var session = Start();
        var deadline = session.Deadline;

        session.Preview(Side.Blue, Id("c05"), T0 + TimeSpan.FromSeconds(10));

        Assert.Empty(session.Actions);
        Assert.Equal(Side.Blue, session.Turn);
        Assert.Equal(deadline, session.Deadline);
        // Ön izlenen şampiyon alınmış sayılmaz: aynısı hâlâ ban'lanabilir.
        Assert.True(session.Ban(Side.Blue, Id("c05"), T0).IsSuccess);
    }

    [Fact]
    public void Temizleme_null_sampiyonla_gecer_ama_sira_yine_sart()
    {
        var session = Start();

        Assert.Equal(0, session.Preview(Side.Blue, null, T0).Value);
        Assert.Equal(DraftErrors.NotYourTurn, session.Preview(Side.Red, null, T0).Error);
    }

    [Fact]
    public void Sirasi_olmayan_takim_on_izleyemez()
    {
        var session = Start();

        Assert.Equal(DraftErrors.NotYourTurn, session.Preview(Side.Red, Id("c05"), T0).Error);
    }

    [Fact]
    public void Alinmis_havuz_disi_ve_fearless_kilitli_sampiyon_on_izlenemez()
    {
        var c20 = Id("c20");
        var session = Start(new FearlessLocks([c20]));
        session.Ban(Side.Blue, Id("c01"), T0);   // sıra kırmızıda

        Assert.Equal(DraftErrors.ChampionUnavailable, session.Preview(Side.Red, Id("c01"), T0).Error);
        Assert.Equal(DraftErrors.NotInPool, session.Preview(Side.Red, Id("teemo"), T0).Error);
        Assert.Equal(DraftErrors.LockedByFearless, session.Preview(Side.Red, c20, T0).Error);
    }

    [Fact]
    public void Suresi_dolan_adimda_ve_swap_fazinda_on_izleme_olmaz()
    {
        var session = Start();
        Assert.Equal(DraftErrors.TurnExpired, session.Preview(Side.Blue, Id("c05"), session.Deadline!.Value).Error);

        PlaySteps(session, 20);
        Assert.Equal(DraftErrors.PhaseClosed, session.Preview(Side.Blue, null, T0).Error);
    }

    [Fact]
    public void Lobi_rengi_koltuktan_bulur_ve_revizyonu_artirmaz()
    {
        var (lobby, host, guest) = StartedDraft();   // ilk maçta A koltuğu (host) mavi
        var revision = lobby.Revision;

        var preview = lobby.Preview(host, Id("c05"), T0);

        Assert.True(preview.IsSuccess, preview.Error?.Code);
        Assert.Equal(new DraftPreview(Side.Blue, 0, Id("c05")), preview.Value);
        Assert.Equal(revision, lobby.Revision);
        // Rakip (kırmızı) sırası değilken ön izleyemez.
        Assert.Equal(DraftErrors.NotYourTurn, lobby.Preview(guest, Id("c05"), T0).Error);
    }

    [Fact]
    public void Lobide_olmayan_ve_draft_baslamadan_on_izleme_yok()
    {
        var host = PlayerId.FromBytes(Enumerable.Repeat((byte)1, 16).ToArray());
        var stranger = PlayerId.FromBytes(Enumerable.Repeat((byte)9, 16).ToArray());
        var lobby = Lobby.Create(Code, host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;

        Assert.Equal(LobbyErrors.NotInLobby, lobby.Preview(stranger, Id("c05"), T0).Error);
        Assert.Equal(DraftErrors.PhaseClosed, lobby.Preview(host, Id("c05"), T0).Error);
    }

    private static (Lobby Lobby, PlayerId Host, PlayerId Guest) StartedDraft()
    {
        var host = PlayerId.FromBytes(Enumerable.Repeat((byte)1, 16).ToArray());
        var guest = PlayerId.FromBytes(Enumerable.Repeat((byte)2, 16).ToArray());
        var lobby = Lobby.Create(Code, host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        lobby.Join(guest, T0);
        lobby.Ready(host, ActionId.Create(Guid.NewGuid()).Value, DraftTimings.Default, T0);
        lobby.Ready(guest, ActionId.Create(Guid.NewGuid()).Value, DraftTimings.Default, T0);
        return (lobby, host, guest);
    }
}
