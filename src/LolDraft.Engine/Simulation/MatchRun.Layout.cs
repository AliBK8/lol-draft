using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Harita düzeni (DESIGN.md §4.11): 14. dakikadan sonra her takım kimin hangi koridorda duracağını seçer ve görünmez
/// <see cref="LayoutEvent"/> ile bildirir. Ekonomi (koridor akışı, CS) rol çiftleri olarak kalır; düzen coğrafyayı
/// belirler: kule fırsatı, koridordaki anlatım (solo kill, sürpriz), gank hedefi, kuşatmanın koridoru, ekranda kim nerede.
/// </summary>
internal sealed partial class MatchRun
{
    private const int LayoutMinMinute = 14;

    // Düzen 2-3 dakikada bir yeniden seçilir; ayrıca bir önceki dakikada objektif ya da kuşatma olduysa.
    // Seçim dakikanın 1. saniyesinde (anlatıcı 25-55, objektif 30, kuşatma 45. saniyede: aynı dakikanın işleri yeni
    // düzeni görür).
    private const int LayoutMinInterval = 2;
    private const int LayoutIntervalSpread = 2;
    private const int LayoutSecond = 1;

    // Taban ağırlıklar: Koridorlar 35, Grup 20, 1-4 30, 1-3-1 15 (MapLayout sırası).
    private static readonly double[] LayoutBaseWeights = [35, 20, 30, 15];

    // Eğilimler (çarpan): splitçisi güçlü (split ≥ 2) olan yan koridora; splitçisi yoksa (split 0) 1-3-1 zor.
    // Altında 2.000+ önde olan bitirmek ister (Grup), geride olan farm ister (Koridorlar). Poke ya da engage toplamı
    // ≥ 7 olan takım grup savaşını sever. 25. dakikadan sonra herkes koridorda dağınık durmaz.
    // Örnek: Fiora (split 3) top'ta, 25. dk, 3.000 önde: Koridorlar 35 × 0,75 × 0,5 ≈ 13, Grup 20 × 1,5 = 30,
    // 1-4 30 × 1,5 = 45, 1-3-1 15 × 1,5 ≈ 22 → en olası 1-4 (%41).
    private const int StrongSplitTag = 2;
    private const double StrongSplitFactor = 1.5;
    private const double NoSplitterOneThreeOneFactor = 0.5;
    private const double LayoutLeadGold = 2000;
    private const double AheadGroupFactor = 1.5;
    private const double AheadLanesFactor = 0.75;
    private const double BehindLanesFactor = 1.3;
    private const double BehindGroupFactor = 0.6;
    private const int GroupTagTotal = 7;
    private const double GroupTagFactor = 1.3;
    private const int LateLayoutMinute = 25;
    private const double LateLanesFactor = 0.5;

    // Standart yerleşim (14. dakikaya kadar herkes): top üst, ormancı ormanında, mid orta, adc + support alt.
    private static readonly Lane?[] StandardPosts = [Lane.Top, null, Lane.Mid, Lane.Bot, Lane.Bot];

    // [taraf]: şimdiki düzen, takım sırasıyla koridorlar ve bir sonraki seçim dakikası.
    private readonly MapLayout[] _layout = [MapLayout.Lanes, MapLayout.Lanes];
    private readonly Lane?[][] _posts = [StandardPosts, StandardPosts];
    private readonly int[] _nextLayoutMinute = [LayoutMinMinute, LayoutMinMinute];

    private IRandomSource LayoutRng => _random[RandomStream.Layout];

    private void ScheduleLayout(int minute)
    {
        if (minute < LayoutMinMinute)
            return;

        var t = (minute - 1) * 60 + LayoutSecond;
        // Bir önceki dakika objektif ya da kuşatma olduysa iki takım da yeniden dizilir.
        var afterFight = _objectiveAt.ContainsKey(minute - 1) || _lastSiegeMinute == minute - 1;
        foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
        {
            if (afterFight || minute >= _nextLayoutMinute[(int)side])
                At(t, () => ChooseLayout(side, minute, t));
        }
    }

    // Zar sırası: düzen, sonraki seçimin aralığı.
    private void ChooseLayout(TeamSide side, int minute, double t)
    {
        var weights = LayoutWeights(side, minute, t);
        var layout = (MapLayout)Pick(weights, LayoutRng.NextDouble());
        _nextLayoutMinute[(int)side] = minute + LayoutMinInterval + LayoutRng.NextIndex(LayoutIntervalSpread);

        var posts = Posts(side, layout, minute);
        // Aynı düzen aynı yerleşimle tekrar seçildiyse olay yok (ekranda kimse kımıldamaz).
        if (layout == _layout[(int)side] && posts.SequenceEqual(_posts[(int)side]))
            return;

        _layout[(int)side] = layout;
        _posts[(int)side] = posts;
        _events.Add(new LayoutEvent(t, "", side, layout, posts));
    }

    private double[] LayoutWeights(TeamSide side, int minute, double t)
    {
        var w = (double[])LayoutBaseWeights.Clone();
        ref var lanes = ref w[(int)MapLayout.Lanes];
        ref var group = ref w[(int)MapLayout.Group];
        ref var oneFour = ref w[(int)MapLayout.OneFour];
        ref var oneThreeOne = ref w[(int)MapLayout.OneThreeOne];

        var team = Team(side);
        var splitTag = Splitter(team).Tags.Split;
        if (splitTag >= StrongSplitTag)
        {
            oneFour *= StrongSplitFactor;
            oneThreeOne *= StrongSplitFactor;
        }
        else if (splitTag == 0)
        {
            oneThreeOne *= NoSplitterOneThreeOneFactor;
        }

        var lead = TeamGold(side, t) - TeamGold(side.Opponent(), t);
        if (lead >= LayoutLeadGold)
        {
            group *= AheadGroupFactor;
            lanes *= AheadLanesFactor;
        }
        else if (lead <= -LayoutLeadGold)
        {
            lanes *= BehindLanesFactor;
            group *= BehindGroupFactor;
        }

        if (team.Champions.Sum(c => c.Tags.Poke) >= GroupTagTotal || team.Champions.Sum(c => c.Tags.Engage) >= GroupTagTotal)
            group *= GroupTagFactor;

        if (minute >= LateLayoutMinute)
            lanes *= LateLanesFactor;

        // Oyun planı (Tactics.cs): Split yan koridorlara, Hızlı bitir gruba yatkın (×2, uyumla ölçekli).
        var tactics = Tactics(side);
        oneFour *= tactics.SplitLayoutFactor;
        oneThreeOne *= tactics.SplitLayoutFactor;
        group *= tactics.GroupLayoutFactor;
        return w;
    }

    // Ağırlıklı seçim: u ∈ [0, 1) toplamın hangi diliminde.
    private static int Pick(double[] weights, double u)
    {
        var x = u * weights.Sum();
        for (var i = 0; i < weights.Length - 1; i++)
        {
            if (x < weights[i])
                return i;
            x -= weights[i];
        }

        return weights.Length - 1;
    }

    // Splitçi: top ve mid'den split etiketi yüksek olan (eşitlikte top).
    private static ChampionProfile Splitter(TeamDraft team) => team.Mid.Tags.Split > team.Top.Tags.Split ? team.Mid : team.Top;

    // Yerleşim, takım sırasıyla (top, jungle, mid, bot, support).
    //   1-4: splitçi top ise üstte, mid ise altta; kalan dört (ormancı hariç) mid'de.
    //   1-3-1: top üstte; mid'in split'i ≥ 2 ise mid altta, değilse adc altta; kalanlar mid'de.
    //   Grup: ormancı dahil beşi tek koridorda.
    private Lane?[] Posts(TeamSide side, MapLayout layout, int minute)
    {
        var team = Team(side);
        return layout switch
        {
            MapLayout.Lanes => StandardPosts,
            MapLayout.OneFour => Splitter(team) == team.Top
                ? [Lane.Top, null, Lane.Mid, Lane.Mid, Lane.Mid]
                : [Lane.Mid, null, Lane.Bot, Lane.Mid, Lane.Mid],
            MapLayout.OneThreeOne => team.Mid.Tags.Split >= StrongSplitTag
                ? [Lane.Top, null, Lane.Bot, Lane.Mid, Lane.Mid]
                : [Lane.Top, null, Lane.Mid, Lane.Bot, Lane.Mid],
            MapLayout.Group => [.. Enumerable.Repeat<Lane?>(GroupLane(side, minute), 5)],
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    // Grubun koridoru: bu ya da sonraki dakika objektif varsa onun tarafı (ejder/Elder alt, Herald/Baron üst); yoksa
    // rakibin en çok kulesini kaybettiği, kulesi kalmış koridor (eşitlikte mid, top, bot). Hepsi düştüyse mid.
    private Lane GroupLane(TeamSide side, int minute)
    {
        foreach (var m in (int[])[minute, minute + 1])
        {
            if (_objectiveAt.TryGetValue(m, out var objective))
            {
                switch (objective)
                {
                    case Objective.Dragon or Objective.Elder:
                        return Lane.Bot;
                    case Objective.Herald or Objective.Baron:
                        return Lane.Top;
                }
            }
        }

        var open = ((Lane[])[Lane.Mid, Lane.Top, Lane.Bot]).Where(l => _towersDestroyed[(int)side, (int)l] < TowersPerLane).ToList();
        return open.Count == 0 ? Lane.Mid : open.MaxBy(l => _towersDestroyed[(int)side, (int)l]);
    }

    // Şampiyonun düzendeki koridoru; boşsa ormanında.
    private Lane? Post(ChampionProfile champion) => _posts[(int)_sideOf[champion.Id]][_slotOf[champion.Id]];

    // Düzene göre koridorda olan ve orada müsait (yaşıyor, başka yerde meşgul değil) şampiyonlar; takım sırasıyla.
    private List<ChampionProfile> PresentIn(TeamSide side, Lane lane, double t) =>
        Team(side).Champions.Where(c => Post(c) == lane && IsInLane(c, t)).ToList();
}
