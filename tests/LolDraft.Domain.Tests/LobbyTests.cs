using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class LobbyTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);

    [Fact]
    public void Lobiyi_acan_A_koltuguna_oturur()
    {
        var host = Player(1);
        var lobby = Create(host);

        Assert.Equal(TeamSlot.A, lobby.SeatOf(host));
        Assert.Equal(host, lobby.PlayerAt(TeamSlot.A));
        Assert.Null(lobby.PlayerAt(TeamSlot.B));
        Assert.False(lobby.IsFull);
    }

    [Fact]
    public void Katilan_B_koltuguna_oturur_ve_lobi_dolar()
    {
        var lobby = Create(Player(1));
        var guest = Player(2);

        var seat = lobby.Join(guest);

        Assert.Equal(TeamSlot.B, seat.Value);
        Assert.Equal(TeamSlot.B, lobby.SeatOf(guest));
        Assert.True(lobby.IsFull);
    }

    [Fact]
    public void Dolu_lobiye_ucuncu_oyuncu_giremez()
    {
        var lobby = Create(Player(1));
        var guest = Player(2);
        lobby.Join(guest);

        var result = lobby.Join(Player(3));

        Assert.Equal(LobbyErrors.Full, result.Error);
        Assert.Null(lobby.SeatOf(Player(3)));
        Assert.Equal(guest, lobby.PlayerAt(TeamSlot.B));
    }

    [Fact]
    public void Ayni_oyuncu_iki_koltuga_oturamaz()
    {
        var host = Player(1);
        var lobby = Create(host);

        Assert.Equal(LobbyErrors.AlreadyInLobby, lobby.Join(host).Error);
        Assert.False(lobby.IsFull);
    }

    [Fact]
    public void Seri_ayarlari_lobi_acilirken_dogrulanir()
    {
        // 40'lık havuzda fearless bo5 sığmaz: lobi hiç oluşmaz.
        var result = Lobby.Create(Code, Player(1), SeriesFormat.Bo5, fearless: true, Pool, TeamSlot.A);

        Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }

    [Fact]
    public void Seri_lobinin_ayarlariyla_baslar()
    {
        var lobby = Lobby.Create(Code, Player(1), SeriesFormat.Bo3, fearless: true, Pool, TeamSlot.B).Value;

        Assert.Equal(SeriesFormat.Bo3, lobby.Series.Format);
        Assert.True(lobby.Series.Fearless);
        Assert.Equal(TeamSlot.B, lobby.Series.BlueTeam);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kisa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA")]      // 21 karakter
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]    // 23 karakter
    [InlineData("AAAAAAAAAAAAAAAAAAAA/A")]     // URL-güvenli olmayan karakter
    [InlineData("AAAAAAAAAAAAAAAAAAAA A")]
    public void Bicimi_bozuk_lobi_kodu_olusmaz(string? value)
    {
        Assert.Equal(LobbyErrors.InvalidLobbyCode, LobbyCode.Create(value).Error);
    }

    [Fact]
    public void Lobi_kodu_16_bayttan_22_karakter_url_guvenli()
    {
        // 0xFB 0xFF... düz base64'te '+' ve '/' üretir; base64url'de '-' ve '_' olur.
        var code = LobbyCode.FromBytes(Enumerable.Repeat((byte)0xFB, 16).ToArray());

        Assert.Equal(22, code.Value.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", code.Value);
        Assert.Equal(code, LobbyCode.Create(code.Value).Value);
    }

    [Fact]
    public void Lobi_kodu_16_bayttan_farkli_uzunlukla_uretilemez()
    {
        Assert.Throws<ArgumentException>(() => LobbyCode.FromBytes(new byte[15]));
        Assert.Throws<ArgumentException>(() => PlayerId.FromBytes(new byte[17]));
    }

    private static Lobby Create(PlayerId host) =>
        Lobby.Create(Code, host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A).Value;

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, 16).ToArray());
}
