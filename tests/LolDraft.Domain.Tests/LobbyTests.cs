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

        var seat = lobby.Join(guest, T0);

        Assert.Equal(TeamSlot.B, seat.Value);
        Assert.Equal(TeamSlot.B, lobby.SeatOf(guest));
        Assert.True(lobby.IsFull);
    }

    [Fact]
    public void Dolu_lobiye_ucuncu_oyuncu_giremez()
    {
        var lobby = Create(Player(1));
        var guest = Player(2);
        lobby.Join(guest, T0);

        var result = lobby.Join(Player(3), T0);

        Assert.Equal(LobbyErrors.Full, result.Error);
        Assert.Null(lobby.SeatOf(Player(3)));
        Assert.Equal(guest, lobby.PlayerAt(TeamSlot.B));
    }

    [Fact]
    public void Ayni_oyuncu_iki_koltuga_oturamaz()
    {
        var host = Player(1);
        var lobby = Create(host);

        Assert.Equal(LobbyErrors.AlreadyInLobby, lobby.Join(host, T0).Error);
        Assert.False(lobby.IsFull);
    }

    [Fact]
    public void Seri_ayarlari_lobi_acilirken_dogrulanir()
    {
        // 40'lık havuzda fearless bo5 sığmaz: lobi hiç oluşmaz.
        var result = Lobby.Create(Code, Player(1), SeriesFormat.Bo5, fearless: true, Pool, TeamSlot.A, T0);

        Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }

    [Fact]
    public void Seri_lobinin_ayarlariyla_baslar()
    {
        var lobby = Lobby.Create(Code, Player(1), SeriesFormat.Bo3, fearless: true, Pool, TeamSlot.B, T0).Value;

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
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA\n")]   // 22 karakter + sonda \n ($ bunu kabul ederdi)
    public void Bicimi_bozuk_lobi_kodu_olusmaz(string? value)
    {
        Assert.Equal(LobbyErrors.InvalidLobbyCode, LobbyCode.Create(value).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kisa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA\n")]
    public void Bicimi_bozuk_oyuncu_kimligi_olusmaz(string? value)
    {
        Assert.Equal(LobbyErrors.InvalidPlayerId, PlayerId.Create(value).Error);
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

    // --- Hazır ve draft hamleleri ---

    [Fact]
    public void Iki_oyuncu_da_hazir_olunca_draft_baslar()
    {
        var (lobby, host, guest) = FullLobby();

        Assert.True(lobby.Ready(host, NewAction(), DraftTimings.Default, T0).IsSuccess);
        Assert.Null(lobby.Series.CurrentDraft);
        Assert.Equal([TeamSlot.A], lobby.ReadySeats);

        Assert.True(lobby.Ready(guest, NewAction(), DraftTimings.Default, T0).IsSuccess);
        Assert.NotNull(lobby.Series.CurrentDraft);
        Assert.Empty(lobby.ReadySeats);
    }

    [Fact]
    public void Rakip_gelmeden_hazir_olunamaz()
    {
        var host = Player(1);
        var lobby = Create(host);

        Assert.Equal(LobbyErrors.NotFull, lobby.Ready(host, NewAction(), DraftTimings.Default, T0).Error);
    }

    [Fact]
    public void Draft_surerken_hazir_denemez()
    {
        var (lobby, host, _) = StartedDraft();

        Assert.Equal(SeriesErrors.GameInProgress, lobby.Ready(host, NewAction(), DraftTimings.Default, T0).Error);
    }

    [Fact]
    public void Draft_baslamadan_hamle_yapilamaz()
    {
        var (lobby, host, _) = FullLobby();

        Assert.Equal(DraftErrors.PhaseClosed, lobby.Ban(host, NewAction(), Id("c01"), T0).Error);
    }

    [Fact]
    public void Koltuk_bu_mactaki_renge_cevrilir()
    {
        // İlk mavi A: A'nın oyuncusu mavi olarak ban'lar, B'nin oyuncusu sırası gelmeden ban'layamaz.
        var (lobby, host, guest) = StartedDraft(firstBlue: TeamSlot.A);

        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(guest, NewAction(), Id("c01"), T0).Error);
        Assert.True(lobby.Ban(host, NewAction(), Id("c01"), T0).IsSuccess);
        Assert.Equal(Side.Blue, lobby.Series.CurrentDraft!.Actions[0].Side);
    }

    [Fact]
    public void Ilk_mavi_B_ise_misafir_baslar()
    {
        var (lobby, host, guest) = StartedDraft(firstBlue: TeamSlot.B);

        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(host, NewAction(), Id("c01"), T0).Error);
        Assert.True(lobby.Ban(guest, NewAction(), Id("c01"), T0).IsSuccess);
    }

    [Fact]
    public void Lobide_olmayan_hamle_yapamaz()
    {
        var (lobby, _, _) = StartedDraft();

        Assert.Equal(LobbyErrors.NotInLobby, lobby.Ban(Player(9), NewAction(), Id("c01"), T0).Error);
        Assert.Equal(LobbyErrors.NotInLobby, lobby.Ready(Player(9), NewAction(), DraftTimings.Default, T0).Error);
    }

    [Fact]
    public void Ayni_hamle_kimligi_ikinci_kez_uygulanmaz()
    {
        var (lobby, host, _) = StartedDraft();
        var action = NewAction();

        Assert.True(lobby.Ban(host, action, Id("c01"), T0).IsSuccess);
        // Çift tıklama: aynı kimlik → aynı sonuç, draft ilerlemez (yoksa ikinci ban "sıra sende değil" derdi).
        Assert.True(lobby.Ban(host, action, Id("c01"), T0).IsSuccess);
        Assert.Single(lobby.Series.CurrentDraft!.Actions);

        // Yeni kimlikle aynı hamle: gerçekten ikinci hamle sayılır ve kurala takılır.
        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(host, NewAction(), Id("c02"), T0).Error);
    }

    [Fact]
    public void Hatali_hamlenin_tekrari_da_ayni_hatayi_doner()
    {
        var (lobby, _, guest) = StartedDraft();
        var action = NewAction();

        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(guest, action, Id("c01"), T0).Error);
        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(guest, action, Id("c01"), T0).Error);
    }

    // --- Lobi saati ---

    [Fact]
    public void Saat_draft_yokken_bir_sey_yapmaz()
    {
        var (lobby, _, _) = FullLobby();

        // Kapanış eşiğinin (kimse yokken 5 dk) altında: draft yok, yapılacak bir şey yok.
        Assert.Equal(LobbyTick.Unchanged, lobby.Advance(T0 + TimeSpan.FromMinutes(4), new FixedRandom(0)));
    }

    [Fact]
    public void Saat_sure_dolmadan_bir_sey_yapmaz()
    {
        var (lobby, _, _) = StartedDraft();

        Assert.Equal(LobbyTick.Unchanged, lobby.Advance(T0 + TimeSpan.FromSeconds(29), new FixedRandom(0)));
        Assert.Empty(lobby.Series.CurrentDraft!.Actions);
    }

    [Fact]
    public void Saat_suresi_dolan_adimi_zaman_asimiyla_oynar()
    {
        var (lobby, host, _) = StartedDraft();
        var expired = T0 + TimeSpan.FromSeconds(30);

        Assert.Equal(LobbyTick.Changed, lobby.Advance(expired, new FixedRandom(0)));

        var draft = lobby.Series.CurrentDraft!;
        var action = Assert.Single(draft.Actions);
        Assert.True(action.ByTimeout);
        Assert.Null(action.Champion);   // ban adımı → boş ban
        Assert.Equal(Side.Red, draft.Turn);
        Assert.Equal(expired + TimeSpan.FromSeconds(30), draft.Deadline);

        // Süre dolduktan sonra gelen hamle reddedilir: saatle yarışan hamle iki kez sayılmaz.
        Assert.Equal(DraftErrors.NotYourTurn, lobby.Ban(host, NewAction(), Id("c01"), expired).Error);
    }

    private static (Lobby Lobby, PlayerId Host, PlayerId Guest) FullLobby(TeamSlot firstBlue = TeamSlot.A)
    {
        var host = Player(1);
        var guest = Player(2);
        var lobby = Lobby.Create(Code, host, SeriesFormat.Bo3, fearless: false, Pool, firstBlue, T0).Value;
        lobby.Join(guest, T0);
        return (lobby, host, guest);
    }

    private static (Lobby Lobby, PlayerId Host, PlayerId Guest) StartedDraft(TeamSlot firstBlue = TeamSlot.A)
    {
        var (lobby, host, guest) = FullLobby(firstBlue);
        lobby.Ready(host, NewAction(), DraftTimings.Default, T0);
        lobby.Ready(guest, NewAction(), DraftTimings.Default, T0);
        return (lobby, host, guest);
    }

    private static ActionId NewAction() => ActionId.Create(Guid.NewGuid()).Value;

    private static Lobby Create(PlayerId host) =>
        Lobby.Create(Code, host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, 16).ToArray());
}
