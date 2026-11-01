namespace LolDraft.Engine.Simulation;

/// <summary>
/// Farm (DESIGN.md §16.4): her 10 sn'lik dilimde koridorda olan, o dilimin minyonlarını keser; altın = CS × 20.
/// Ezilen (o dakikanın Δ'sına göre) CS kaçırır, kaçanın yarısını ezen keser. Ölü ya da başka yerde olan kesemez.
/// Zar atmaz.
/// </summary>
internal sealed partial class MatchRun
{
    // Dakikada 12 minyon (2 dalga × 6), 6 dilimde; normal tempoda %70'i kesilir → dilim başı 1,4 CS (dk'da 8,4).
    private const int FarmSamplesPerMinute = 6;
    private const double MinionsPerMinute = 12;
    private const double NormalFarmRate = 0.7;
    private const double GoldPerCs = 20;

    // Ezilenin kaçırdığı oran: 0,25 × |Δ|, en fazla yarısı. Kaçanın yarısını ezen keser, yarısı kuleye gider.
    private const double MissPerDelta = 0.25;
    private const double MaxMissShare = 0.5;
    private const double StealShare = 0.5;

    // Support CS kesmez; support item'ı yaşarken dakikada 90 altın (dilim başı 15).
    private const double SupportItemGoldPerMinute = 90;

    private static readonly double BaseCsPerSample = MinionsPerMinute * NormalFarmRate / FarmSamplesPerMinute;

    // Bu dakikanın koridor ve orman Δ'ları (ScheduleLanes / ScheduleJungle yazar). Pozitif = Mavi önde.
    private readonly double[] _laneDelta = new double[Lanes.Length];
    private double _jungleDelta;

    // Dilimler dakikanın 5, 15, ..., 55. saniyesinde. En son kaydolur: aynı saniyedeki diğer işlerden sonra çözülür.
    private void ScheduleFarm(int minute)
    {
        var t0 = (minute - 1) * 60;
        for (var i = 0; i < FarmSamplesPerMinute; i++)
        {
            var t = t0 + 5 + i * 10;
            At(t, () => FarmSample(t));
        }
    }

    private void FarmSample(double t)
    {
        foreach (var lane in Lanes)
            FarmPair(LaneChampions(TeamSide.Blue, lane)[0], LaneChampions(TeamSide.Red, lane)[0], _laneDelta[(int)lane], t);

        // Ormancı kamp keser; orman akışında önde olan rakibin kamplarından da çalar (aynı kural).
        FarmPair(_blue.Jungle, _red.Jungle, _jungleDelta, t);

        foreach (var support in (ChampionProfile[])[_blue.Support, _red.Support])
        {
            if (_deaths.IsAlive(support, t))
                _gold.Earn(support, GoldSource.SupportItem, SupportItemGoldPerMinute / FarmSamplesPerMinute);
        }
    }

    // Karşılıklı iki farmcı: koridorda (yaşıyor, başka yerde değil) olan kendi payını keser.
    private void FarmPair(ChampionProfile blue, ChampionProfile red, double delta, double t)
    {
        var (blueCs, redCs) = SplitCs(BaseCsPerSample, delta, IsInLane(blue, t), IsInLane(red, t));
        AddFarm(blue, blueCs);
        AddFarm(red, redCs);
    }

    private void AddFarm(ChampionProfile champion, double cs)
    {
        if (cs == 0)
            return;
        _gold.AddCs(champion, cs);
        _gold.Earn(champion, GoldSource.Farm, cs * GoldPerCs);
    }

    // Bir dilimde iki tarafın CS'si:
    //   kaçan = taban × min(0,25 × |Δ|, 0,5)
    //   ezilen = taban − kaçan,  ezen = taban + 0,5 × kaçan
    // Örnek: Δ = 0,7, taban 1,4 → kaçan 0,245: ezilen 1,155, ezen 1,5225. Koridorda olmayan 0 keser.
    internal static (double Blue, double Red) SplitCs(double baseCs, double delta, bool bluePresent, bool redPresent)
    {
        var missed = baseCs * Math.Min(MissPerDelta * Math.Abs(delta), MaxMissShare);
        var winner = baseCs + StealShare * missed;
        var loser = baseCs - missed;
        var (blue, red) = delta >= 0 ? (winner, loser) : (loser, winner);
        return (bluePresent ? blue : 0, redPresent ? red : 0);
    }
}
