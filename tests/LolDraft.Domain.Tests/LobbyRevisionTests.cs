using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>
/// Durum sürümü (DESIGN.md §8.12): her görünür değişimde bir artar, değişim yoksa sabit kalır.
/// İstemci yayınlar ters sırayla gelse de en yeni durumu bu sayıyla seçer.
/// </summary>
public sealed class LobbyRevisionTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);
    private static readonly IDraftRandom Random = new FixedRandom(0);

    private readonly PlayerId _host = Player(1);
    private readonly PlayerId _guest = Player(2);

    [Fact]
    public void Her_gorunur_degisimde_surum_bir_artar()
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        var start = lobby.Revision;

        lobby.Join(_guest, T0);
        Assert.Equal(start + 1, lobby.Revision);

        lobby.Connect(_host, T0);
        Assert.Equal(start + 2, lobby.Revision);

        lobby.Ready(_host, NewAction(), DraftTimings.Default, T0);
        Assert.Equal(start + 3, lobby.Revision);

        lobby.Disconnect(_host, T0);
        Assert.Equal(start + 4, lobby.Revision);
    }

    [Fact]
    public void Basarisiz_ya_da_tekrar_gelen_hamle_surumu_degistirmez()
    {
        var lobby = StartedDraft();
        var before = lobby.Revision;

        // Sıra Mavi'de (A): B'nin hamlesi reddedilir.
        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(_guest, NewAction(), Id("c01"), T0).Error);
        Assert.Equal(before, lobby.Revision);

        var action = NewAction();
        Assert.True(lobby.Ban(_host, action, Id("c01"), T0).IsSuccess);
        Assert.Equal(before + 1, lobby.Revision);

        // Çift tıklama: aynı kimlik ilk sonucu döndürür, durum değişmez.
        Assert.True(lobby.Ban(_host, action, Id("c01"), T0).IsSuccess);
        Assert.Equal(before + 1, lobby.Revision);
    }

    [Fact]
    public void Saat_sadece_durumu_degistirdiginde_surumu_artirir()
    {
        var lobby = StartedDraft();
        var before = lobby.Revision;

        Assert.Equal(LobbyTick.Unchanged, lobby.Advance(T0 + TimeSpan.FromSeconds(10), Random));
        Assert.Equal(before, lobby.Revision);

        // Adımın süresi doldu: boş ban, durum değişti.
        Assert.Equal(LobbyTick.Changed, lobby.Advance(T0 + TimeSpan.FromSeconds(30), Random));
        Assert.Equal(before + 1, lobby.Revision);
    }

    /// <summary>İkisi de bağlı ve hazır, A mavi: draft T0'da başladı, sıra A'da.</summary>
    private Lobby StartedDraft()
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        lobby.Join(_guest, T0);
        lobby.Connect(_host, T0);
        lobby.Connect(_guest, T0);
        lobby.Ready(_host, NewAction(), DraftTimings.Default, T0);
        lobby.Ready(_guest, NewAction(), DraftTimings.Default, T0);
        Assert.NotNull(lobby.Series.CurrentDraft);
        return lobby;
    }

    private static ActionId NewAction() => ActionId.Create(Guid.NewGuid()).Value;

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, 16).ToArray());
}
