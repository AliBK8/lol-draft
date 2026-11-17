using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>Koltuk anahtarıyla geri dönme (DESIGN.md §8.4): anahtar özeti eşleşirse aynı oyuncu, aynı koltuk.</summary>
public sealed class LobbyRejoinTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);

    private readonly PlayerId _host = Player(1);
    private readonly PlayerId _guest = Player(2);

    [Fact]
    public void Dogru_anahtar_ayni_oyuncuyu_ve_koltugu_dondurur()
    {
        var lobby = FullLobby();

        var back = lobby.Rejoin(Key(20), Key(21), T0);

        Assert.True(back.IsSuccess);
        Assert.Equal((_guest, TeamSlot.B), back.Value);
    }

    // Tek kullanımlık: kullanılan anahtar yenisiyle değişir; eskisi bir daha çalışmaz, yenisi çalışır.
    [Fact]
    public void Anahtar_tek_kullanimlik_ve_yenisiyle_degisir()
    {
        var lobby = FullLobby();
        lobby.Rejoin(Key(10), Key(11), T0);

        Assert.Equal(LobbyErrors.RejoinRejected, lobby.Rejoin(Key(10), Key(12), T0).Error);
        Assert.Equal((_host, TeamSlot.A), lobby.Rejoin(Key(11), Key(12), T0).Value);
    }

    [Fact]
    public void Yanlis_anahtar_reddedilir_ve_hicbir_seyi_degistirmez()
    {
        var lobby = FullLobby();
        var revision = lobby.Revision;

        Assert.Equal(LobbyErrors.RejoinRejected, lobby.Rejoin(Key(99), Key(98), T0).Error);
        Assert.Equal(revision, lobby.Revision);
        Assert.True(lobby.Rejoin(Key(10), Key(11), T0).IsSuccess);   // gerçek anahtarlar bozulmadı
        Assert.True(lobby.Rejoin(Key(20), Key(21), T0).IsSuccess);
    }

    // Yeni anahtar verilince eskisi geçersiz (her koltukta en fazla bir anahtar).
    [Fact]
    public void Yeni_anahtar_eskisini_gecersiz_kilar()
    {
        var lobby = FullLobby();
        lobby.AssignRejoinKey(_host, Key(30));

        Assert.Equal(LobbyErrors.RejoinRejected, lobby.Rejoin(Key(10), Key(31), T0).Error);
        Assert.True(lobby.Rejoin(Key(30), Key(31), T0).IsSuccess);
    }

    [Fact]
    public void Lobide_olmayana_anahtar_verilmez()
    {
        var lobby = FullLobby();

        Assert.Equal(LobbyErrors.NotInLobby, lobby.AssignRejoinKey(Player(9), Key(40)).Error);
        Assert.Equal(LobbyErrors.RejoinRejected, lobby.Rejoin(Key(40), Key(41), T0).Error);
    }

    // Geri dönüş bir etkinliktir: hareketsizlik sayacını sıfırlar (görünür durumu değiştirmez, sürüm artmaz).
    [Fact]
    public void Geri_donus_hareketsizlik_sayacini_sifirlar()
    {
        var lobby = FullLobby();
        var later = T0 + TimeSpan.FromMinutes(10);

        lobby.Rejoin(Key(10), Key(11), later);

        Assert.Equal(later, lobby.LastActivity);
    }

    [Fact]
    public void Ozet_metne_dokulmez() => Assert.DoesNotContain(Key(1).Value, Key(1).ToString());

    private Lobby FullLobby()
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        lobby.Join(_guest, T0);
        lobby.AssignRejoinKey(_host, Key(10));
        lobby.AssignRejoinKey(_guest, Key(20));
        return lobby;
    }

    private static RejoinKey Key(byte n) => RejoinKey.FromBytes(Enumerable.Repeat(n, RejoinKey.ByteLength).ToArray());

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, 16).ToArray());
}
