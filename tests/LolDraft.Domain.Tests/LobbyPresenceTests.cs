using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>Bağlantı takibi, hükmen galibiyet ve kapanış (DESIGN.md §8.14). Saat elle verilir.</summary>
public sealed class LobbyPresenceTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);
    private static readonly IDraftRandom Random = new FixedRandom(0);

    private readonly PlayerId _host = Player(1);
    private readonly PlayerId _guest = Player(2);

    // --- Bağlantı sayımı ---

    [Fact]
    public void Oturan_oyuncu_baglanana_kadar_yok_sayilir()
    {
        var lobby = FullLobby();

        Assert.False(lobby.IsConnected(TeamSlot.A));
        Assert.Equal(T0, lobby.AbsentSince(TeamSlot.A));

        lobby.Connect(_host, T0 + Seconds(5));

        Assert.True(lobby.IsConnected(TeamSlot.A));
        Assert.Null(lobby.AbsentSince(TeamSlot.A));
    }

    [Fact]
    public void Iki_sekmeden_biri_kapaninca_oyuncu_hala_bagli()
    {
        var lobby = FullLobby();
        lobby.Connect(_host, T0);
        lobby.Connect(_host, T0);

        lobby.Disconnect(_host, T0 + Seconds(10));
        Assert.True(lobby.IsConnected(TeamSlot.A));

        lobby.Disconnect(_host, T0 + Seconds(20));
        Assert.False(lobby.IsConnected(TeamSlot.A));
        Assert.Equal(T0 + Seconds(20), lobby.AbsentSince(TeamSlot.A));
    }

    [Fact]
    public void Lobide_olmayanin_baglantisi_sayilmaz()
    {
        var lobby = FullLobby();

        Assert.Equal(LobbyErrors.NotInLobby, lobby.Connect(Player(9), T0).Error);
        Assert.Equal(LobbyErrors.NotInLobby, lobby.Disconnect(Player(9), T0).Error);
    }

    // --- Hükmen galibiyet ---

    [Fact]
    public void Seri_basladiktan_sonra_2_dk_yok_olan_hukmen_kaybeder()
    {
        var lobby = StartedSeries();
        var left = T0 + Seconds(10);
        lobby.Disconnect(_guest, left);

        Assert.Equal(LobbyTick.Changed, Tick(lobby, left + Seconds(29)));   // draft adımı zaman aşımı, hükmen değil
        Assert.Null(lobby.Series.Winner);

        Assert.Equal(LobbyTick.Changed, Tick(lobby, left + Lobby.ForfeitAfter));

        Assert.Equal(TeamSlot.A, lobby.Series.Winner);
        Assert.Equal(SeriesEndReason.Forfeit, lobby.Series.EndReason);
        Assert.Equal(TeamSlot.B, lobby.Series.ForfeitedBy);
        Assert.Null(lobby.Series.CurrentDraft);   // yarım draft atıldı
    }

    [Fact]
    public void Iki_dakika_dolmadan_donen_oyuncu_kaybetmez()
    {
        var lobby = StartedSeries();
        var left = T0 + Seconds(10);
        lobby.Disconnect(_guest, left);
        lobby.Connect(_guest, left + Seconds(90));

        Tick(lobby, left + Lobby.ForfeitAfter + Seconds(1));

        Assert.Null(lobby.Series.Winner);
    }

    [Fact]
    public void Seri_baslamadan_kopan_hukmen_kaybetmez()
    {
        var lobby = FullLobby();
        lobby.Connect(_host, T0);
        lobby.Connect(_guest, T0);
        lobby.Disconnect(_guest, T0);

        Assert.Equal(LobbyTick.Unchanged, Tick(lobby, T0 + Lobby.ForfeitAfter + Seconds(1)));
        Assert.Null(lobby.Series.Winner);
    }

    [Fact]
    public void Ikisi_birden_yoksa_kimse_kazanmaz()
    {
        var lobby = StartedSeries();
        lobby.Disconnect(_guest, T0);
        lobby.Disconnect(_host, T0 + Seconds(30));

        Tick(lobby, T0 + Lobby.ForfeitAfter + Seconds(1));

        Assert.Null(lobby.Series.Winner);
    }

    [Fact]
    public void Biten_seride_hukmen_islemez()
    {
        var lobby = StartedSeries();
        lobby.Series.Forfeit(TeamSlot.A);
        lobby.Disconnect(_host, T0);

        Tick(lobby, T0 + Lobby.ForfeitAfter + Seconds(1));

        Assert.Equal(TeamSlot.B, lobby.Series.Winner);   // ilk karar değişmez
    }

    // --- Kapanış ---

    [Fact]
    public void Kimse_bagli_degilse_son_cikistan_5_dk_sonra_kapanir()
    {
        var lobby = StartedSeries();
        lobby.Disconnect(_guest, T0);
        var lastLeft = T0 + Seconds(30);
        lobby.Disconnect(_host, lastLeft);

        Assert.NotEqual(LobbyTick.Closed, Tick(lobby, lastLeft + (Lobby.EmptyCloseAfter - Seconds(1))));
        Assert.Equal(LobbyTick.Closed, Tick(lobby, lastLeft + Lobby.EmptyCloseAfter));
    }

    [Fact]
    public void Lobiyi_acip_hic_baglanmayan_lobi_5_dk_sonra_kapanir()
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo1, fearless: false, Pool, TeamSlot.A, T0).Value;

        Assert.Equal(LobbyTick.Closed, Tick(lobby, T0 + Lobby.EmptyCloseAfter));
    }

    [Fact]
    public void Bagli_olsa_da_30_dk_hareketsiz_lobi_kapanir()
    {
        var lobby = FullLobby();
        lobby.Connect(_host, T0);
        lobby.Connect(_guest, T0);

        Assert.Equal(LobbyTick.Unchanged, Tick(lobby, T0 + (Lobby.IdleCloseAfter - Seconds(1))));
        Assert.Equal(LobbyTick.Closed, Tick(lobby, T0 + Lobby.IdleCloseAfter));
    }

    [Fact]
    public void Hamle_hareketsizlik_sayacini_sifirlar()
    {
        var lobby = FullLobby();
        lobby.Connect(_host, T0);
        lobby.Connect(_guest, T0);
        var later = T0 + (Lobby.IdleCloseAfter - Seconds(10));

        lobby.Ready(_host, NewAction(), DraftTimings.Default, later);

        Assert.Equal(later, lobby.LastActivity);
        Assert.NotEqual(LobbyTick.Closed, Tick(lobby, T0 + Lobby.IdleCloseAfter));
    }

    // --- Seri ---

    [Fact]
    public void Hukmen_biten_seriye_mac_yazilamaz_draft_baslatilamaz()
    {
        var series = Series.Start(SeriesFormat.Bo3, false, Pool, TeamSlot.A).Value;

        Assert.True(series.Forfeit(TeamSlot.A).IsSuccess);

        Assert.Equal(TeamSlot.B, series.Winner);
        Assert.Equal(SeriesErrors.SeriesFinished, series.Forfeit(TeamSlot.B).Error);
        Assert.Equal(SeriesErrors.SeriesFinished, series.StartDraft(DraftTimings.Default, T0).Error);
        Assert.Equal(SeriesErrors.SeriesFinished, series.RecordGame(Side.Blue, NewMatch()).Error);
    }

    private Lobby FullLobby()
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        lobby.Join(_guest, T0);
        return lobby;
    }

    /// <summary>İkisi de bağlı, ikisi de hazır: draft T0'da başladı (ilk adım T0 + 30 sn'de biter).</summary>
    private Lobby StartedSeries()
    {
        var lobby = FullLobby();
        lobby.Connect(_host, T0);
        lobby.Connect(_guest, T0);
        lobby.Ready(_host, NewAction(), DraftTimings.Default, T0);
        lobby.Ready(_guest, NewAction(), DraftTimings.Default, T0);
        Assert.True(lobby.Series.HasStarted);
        return lobby;
    }

    private static LobbyTick Tick(Lobby lobby, Instant now) => lobby.Advance(now, Random);

    private static TimeSpan Seconds(int value) => TimeSpan.FromSeconds(value);

    private static ActionId NewAction() => ActionId.Create(Guid.NewGuid()).Value;

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, 16).ToArray());
}
