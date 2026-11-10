using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class SeriesTests
{
    [Fact]
    public void Bo3_renkler_her_mac_degisir_skor_takima_yazilir()
    {
        var series = Start(SeriesFormat.Bo3, firstBlue: TeamSlot.A);

        // Maç 1: A mavi, mavi kazanır → 1-0.
        Assert.Equal(TeamSlot.A, series.BlueTeam);
        PlayGame(series, Side.Blue);
        Assert.Equal((1, 0), Score(series));

        // Maç 2: B mavi, mavi kazanır → galibiyet B'ye, 1-1.
        Assert.Equal(TeamSlot.B, series.BlueTeam);
        Assert.Equal(Side.Red, series.SideOf(TeamSlot.A));
        PlayGame(series, Side.Blue);
        Assert.Equal((1, 1), Score(series));
        Assert.False(series.IsFinished);

        // Maç 3: A yine mavi, kırmızı kazanır → B seriyi 2-1 alır.
        Assert.Equal(TeamSlot.A, series.BlueTeam);
        PlayGame(series, Side.Red);
        Assert.Equal((1, 2), Score(series));
        Assert.Equal(TeamSlot.B, series.Winner);
    }

    [Fact]
    public void Ilk_mavi_disaridan_gelir()
    {
        var series = Start(SeriesFormat.Bo3, firstBlue: TeamSlot.B);

        Assert.Equal(TeamSlot.B, series.BlueTeam);
        Assert.Equal(TeamSlot.A, series.TeamOn(Side.Red));
    }

    [Fact]
    public void Bo1_tek_macta_biter()
    {
        var series = Start(SeriesFormat.Bo1);

        PlayGame(series, Side.Red);

        Assert.Equal(TeamSlot.B, series.Winner);
        Assert.Single(series.Games);
    }

    [Fact]
    public void Bo5_uc_galibiyette_erken_biter_ve_sonrasi_reddedilir()
    {
        var series = Start(SeriesFormat.Bo5);

        // A: mavi, kırmızı, mavi olarak kazanır → 3-0.
        PlayGame(series, Side.Blue);
        PlayGame(series, Side.Red);
        PlayGame(series, Side.Blue);

        Assert.Equal(TeamSlot.A, series.Winner);
        Assert.Equal(SeriesErrors.SeriesFinished, series.StartDraft(DraftTimings.Default, T0).Error);
        Assert.Equal(SeriesErrors.SeriesFinished, series.RecordGame(Side.Blue).Error);
        Assert.Equal(3, series.Games.Count);
    }

    [Fact]
    public void Mac_kaydi_kim_maviydi_kim_kazandi_tutar()
    {
        var series = Start(SeriesFormat.Bo3);

        PlayGame(series, Side.Red);
        PlayGame(series, Side.Red);

        Assert.Equal([(1, TeamSlot.A, TeamSlot.B), (2, TeamSlot.B, TeamSlot.A)],
            series.Games.Select(g => (g.Number, g.Blue, g.Winner)));
    }

    [Fact]
    public void Fearless_onceki_maclarin_pickleri_kilitlenir_banlar_tasinmaz()
    {
        var series = Start(SeriesFormat.Bo3, fearless: true);
        Assert.Empty(series.Locks.Champions);

        var first = PlayGame(series, Side.Blue);

        var picks = Champions(first, DraftActionKind.Pick);
        var bans = Champions(first, DraftActionKind.Ban);
        Assert.Equal(10, picks.Length);
        Assert.True(series.Locks.Champions.SetEquals(picks));
        Assert.DoesNotContain(bans, series.Locks.Contains);

        // Maç 2'nin draft'ı bu kilitlerle başlar: maç 1'in pick'i seçilemez, ban'ı yine ban'lanabilir.
        var draft = series.StartDraft(DraftTimings.Default, T0).Value;
        Assert.Equal(DraftErrors.LockedByFearless, draft.Ban(Side.Blue, picks[0], T0).Error);
        Assert.True(draft.Ban(Side.Blue, bans[0], T0).IsSuccess);
    }

    [Fact]
    public void Fearless_bo3_40lik_havuza_tam_sigar()
    {
        var series = Start(SeriesFormat.Bo3, fearless: true);

        PlayGame(series, Side.Blue);
        PlayGame(series, Side.Blue);
        Assert.Equal(20, series.Locks.Champions.Count);

        // 3. maç: kalan 20 şampiyon = 10 ban + 10 pick.
        PlayGame(series, Side.Blue);
        Assert.True(series.IsFinished);
    }

    [Fact]
    public void Fearless_kapaliyken_kilit_olusmaz()
    {
        var series = Start(SeriesFormat.Bo3);

        PlayGame(series, Side.Blue);

        Assert.Empty(series.Locks.Champions);
    }

    [Theory]
    [InlineData(SeriesFormat.Bo1, true, 20, true)]
    [InlineData(SeriesFormat.Bo1, true, 19, false)]
    [InlineData(SeriesFormat.Bo3, true, 40, true)]
    [InlineData(SeriesFormat.Bo3, true, 39, false)]
    [InlineData(SeriesFormat.Bo5, true, 40, false)]   // son maçtan önce 40 kilit
    [InlineData(SeriesFormat.Bo5, true, 60, true)]    // havuz büyürse kendiliğinden açılır
    [InlineData(SeriesFormat.Bo5, false, 40, true)]
    [InlineData(SeriesFormat.Bo5, false, 19, false)]
    public void Havuz_serinin_en_uzun_haline_yetmeli(SeriesFormat format, bool fearless, int poolSize, bool fits)
    {
        var pool = Enumerable.Range(1, poolSize).Select(i => Id($"c{i:00}"));

        var result = Series.Start(format, fearless, pool, TeamSlot.A);

        Assert.Equal(fits, result.IsSuccess);
        if (!fits)
            Assert.Equal(DraftErrors.PoolTooSmall, result.Error);
    }

    [Fact]
    public void Mac_surerken_ikinci_draft_baslatilamaz()
    {
        var series = Start(SeriesFormat.Bo3);
        var draft = series.StartDraft(DraftTimings.Default, T0).Value;

        Assert.Equal(SeriesErrors.GameInProgress, series.StartDraft(DraftTimings.Default, T0).Error);
        Assert.Same(draft, series.CurrentDraft);
    }

    [Fact]
    public void Draft_bitmeden_sonuc_yazilamaz()
    {
        var series = Start(SeriesFormat.Bo3);

        // Draft hiç başlamadı.
        Assert.Equal(SeriesErrors.DraftNotCompleted, series.RecordGame(Side.Blue).Error);

        // Draft başladı ama swap'ta, henüz kilitlenmedi.
        var draft = series.StartDraft(DraftTimings.Default, T0).Value;
        PlaySteps(draft, 20);
        Assert.Equal(SeriesErrors.DraftNotCompleted, series.RecordGame(Side.Blue).Error);

        Assert.Equal((0, 0), Score(series));
        Assert.Empty(series.Games);
    }

    [Fact]
    public void Ayni_mac_iki_kez_yazilamaz()
    {
        var series = Start(SeriesFormat.Bo5);
        PlayGame(series, Side.Blue);

        Assert.Equal(SeriesErrors.DraftNotCompleted, series.RecordGame(Side.Blue).Error);
        Assert.Equal((1, 0), Score(series));
    }

    private static Series Start(SeriesFormat format, bool fearless = false, TeamSlot firstBlue = TeamSlot.A) =>
        Series.Start(format, fearless, Pool, firstBlue).Value;

    private static (int A, int B) Score(Series series) => (series.Wins(TeamSlot.A), series.Wins(TeamSlot.B));

    private static ChampionId[] Champions(DraftResult draft, DraftActionKind kind) =>
        draft.Actions.Where(a => a.Kind == kind).Select(a => a.Champion!.Value).ToArray();

    /// <summary>Bir maçı baştan sona oynar: draft (her adımda ilk uygun şampiyon), kilit, sonuç.</summary>
    private static DraftResult PlayGame(Series series, Side winner)
    {
        var locks = series.Locks;
        var draft = series.StartDraft(DraftTimings.Default, T0).Value;

        while (draft.CurrentStep is { } step)
        {
            var champion = Pool.First(c => !locks.Contains(c) && draft.Actions.All(a => a.Champion != c));
            var result = step.Kind == DraftActionKind.Ban
                ? draft.Ban(step.Side, champion, T0)
                : draft.Pick(step.Side, champion, T0);
            Assert.True(result.IsSuccess, result.Error?.Code);
        }

        Assert.True(draft.LockLineup(Side.Blue, T0).IsSuccess);
        Assert.True(draft.LockLineup(Side.Red, T0).IsSuccess);

        var outcome = draft.Outcome!;
        Assert.True(series.RecordGame(winner).IsSuccess);
        return outcome;
    }
}
