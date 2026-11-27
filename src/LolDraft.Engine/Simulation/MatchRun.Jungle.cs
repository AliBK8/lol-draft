using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Orman akışı, gank ve support roam'u (DESIGN.md §4.5 Orman).</summary>
internal sealed partial class MatchRun
{
    // Orman akışı: koridorun yarısı kadar etki, daha az gürültü.
    private const double JunglePowerFactor = 0.05;
    private const double JungleNoiseSigma = 0.3;

    // Gank ayarları.
    private const double GankSideDivisor = 4;
    private const int SupportRoamBeforeMinute = 14;
    private const double SupportRoamChance = 0.45;
    private const double GankSuccessSolo = 0.6;
    private const double GankSuccessWithSupport = 0.75;
    private const double GankWinValueSolo = 4;
    private const double GankWinValueWithSupport = 5;
    private const double GankFailValue = 2;

    // Hareket ipucu: katılımcılar 14 sn önce yola çıkar, olaydan sonra 4 sn kalır.
    private const int GankMoveLead = 14;
    private const int GankMoveTail = 4;

    // Gank pencereleri (dk, dahil; dakika 4 = 3:00-3:59): her pencerede bir gank. Dakika ve saniye pencerenin ilk
    // dakikasında zarla seçilir: gank saat gibi işlemesin (eskiden hep 2:40, 5:40, 8:40...; izleyen bir sonrakini
    // biliyordu). İlk pencere 3:10-3:55: ormancı ilk turunu ~3:15'te bitirir (ön yüzdeki orman programı).
    private static readonly (int From, int To)[] GankWindows = [(4, 4), (6, 7), (9, 10), (12, 13), (17, 18), (23, 24)];
    private const double GankMinSecond = 10;
    private const double GankSecondSpan = 45;

    // Aynı dakikada objektif varsa (savaş 30. sn, 5 sn sürer) gank en erken 50. sn: ormancı 14 sn önce yola çıkar,
    // objektif savaşı bitmeden ayrılmasın.
    private const double GankAfterObjectiveSecond = 50;

    // Pencerenin başında seçilen gank dakikaları → dakika içindeki saniye.
    private readonly Dictionary<int, double> _gankAt = [];

    // Erken baskı planının ek gank dakikaları (normal pencerelerin arası): gank'ı planın takımı atar (Tactics.cs).
    private static readonly int[] ExtraGankMinutes = [8, 11];

    // Orman akışı dakika başında puana yazılır; gank kendi saniyesine kaydolur.
    // Zar sırası: orman gürültüsü, (pencere başındaysa) gank dakikası ve saniyesi, (dk 5) istila,
    // (Erken baskı planı, dk 8 ve 11) Mavi'nin sonra Kırmızı'nın ek gank'ı: olacak mı, saniye.
    // Plan yoksa ek zar yok (Dengeli bit bit aynı).
    private void ScheduleJungle(int minute)
    {
        var t0 = (minute - 1) * 60;
        var powerDiff = EffectivePower(_blue.Jungle, minute, t0) - EffectivePower(_red.Jungle, minute, t0);

        // orman += 0.05 × (G_eff_jg_Mavi − G_eff_jg_Kırmızı) + N(0, 0.3) + istila temposu (±1, 3 dk; MatchRun.Invade.cs)
        // Rol dışı ormancı varsa lane fazında gürültü × 1,5 (OffRole).
        _jungleDelta = JunglePowerFactor * powerDiff
                       + JungleRng.NextNormal() * JungleNoiseSigma * Volatility(minute, _blue.Jungle, _red.Jungle)
                       + InvadeTempoAt(minute);
        _scores.Add(ScoreCategory.Jungle, _jungleDelta);

        foreach (var (from, to) in GankWindows)
        {
            if (minute == from)
                _gankAt[from + JungleRng.NextIndex(to - from + 1)] = GankMinSecond + JungleRng.NextDouble() * GankSecondSpan;
        }

        ScheduleInvade(minute, powerDiff);

        if (ExtraGankMinutes.Contains(minute))
        {
            foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
            {
                var chance = Tactics(side).ExtraGankChance;
                if (chance > 0 && JungleRng.NextDouble() < chance)
                    ScheduleGank(minute, GankMinSecond + JungleRng.NextDouble() * GankSecondSpan, powerDiff, side);
            }
        }

        if (_gankAt.TryGetValue(minute, out var second))
            ScheduleGank(minute, second, powerDiff, side: null);
    }

    // side: gank'ı atan belli (Erken baskı'nın ek gank'ı) ya da null (ormancı gücüne göre zar).
    private void ScheduleGank(int minute, double second, double powerDiff, TeamSide? side)
    {
        // Objektifli dakikada aynı zar 50-55'e sıkıştırılır (tek saniyeye yığılmasın).
        if (_objectiveAt.ContainsKey(minute))
            second = GankAfterObjectiveSecond + (second - GankMinSecond) / GankSecondSpan * (60 - 5 - GankAfterObjectiveSecond);
        var t = (minute - 1) * 60 + second;
        At(t, () => PlayGank(minute, t, powerDiff, side));
    }

    // Zar sırası: (atan belli değilse) taraf, koridor, (şartlar tutarsa) support katılımı, başarı.
    private void PlayGank(int minute, double t, double powerDiff, TeamSide? forcedSide)
    {
        // Gank'ı atan taraf: p_Mavi = clamp(1 / (1 + e^(−(G_eff_jg farkı)/4))). Eşit ormancıda %50.
        // Prototipte sıkıştırılmıyordu (fark 9'da ~%90.5); her olay ihtimali gibi %10-%90.
        var blueProbability = Probability.Clamp(Probability.Logistic(powerDiff / GankSideDivisor));
        var side = forcedSide ?? (JungleRng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red);

        // Koridor: üçü eşit. Bot baskısı planında bot'a ~%73 (uyumla 0,63-0,83), kalan top ve mid'e eşit.
        var botShare = Tactics(side).BotGankShare;
        var lane = botShare > 0
            ? JungleRng.NextDouble() < botShare ? Lane.Bot : JungleRng.NextIndex(2) == 0 ? Lane.Top : Lane.Mid
            : Lanes[JungleRng.NextIndex(Lanes.Length)];
        var team = Team(side);
        var jungler = team.Jungle;
        var support = team.Support;

        // Koridorda yaşayan ilk rakip hedeftir; kimse yoksa ya da ormancı ölüyse gank olmaz.
        var victim = LaneChampions(side.Opponent(), lane).FirstOrDefault(c => _deaths.IsAlive(c, t));
        if (victim is null || !_deaths.IsAlive(jungler, t))
            return;

        // Support sadece lane fazında ve bot dışındaki koridora katılır (bot'ta zaten orada).
        // Kısa devre sırası önemli: önceki şartlardan biri tutmazsa zar atılmaz (JS && ile aynı).
        var withSupport = minute < SupportRoamBeforeMinute
                          && lane != Lane.Bot
                          && _deaths.IsAlive(support, t)
                          && JungleRng.NextDouble() < SupportRoamChance;

        // Gank'a giden support bot koridorundan çıkmış olur.
        if (withSupport)
            MarkAway([support], t + AwayAfterRoamSeconds);

        string[] parts = withSupport ? [jungler.Id, support.Id] : [jungler.Id];
        _events.Add(new MoveEvent(t, "", parts, lane, River: null, GankMoveLead, GankMoveTail));

        // Başarı: tek başına %60, support'la %75; K7: ± %5 × (gelenlerin pick toplamı − hedefin mob'u), ±%15.
        // Örnek: Lee Sin (pick 3) Kayle'a (mob 1) tek başına → %70.
        var gankers = withSupport ? new[] { jungler, support } : [jungler];
        var pickBonus = TeamRules.GankPickBonus(gankers, victim);
        ReportGankRule(side, jungler, pickBonus);
        var success = (withSupport ? GankSuccessWithSupport : GankSuccessSolo) + pickBonus;
        // Bot baskısı planı: bot'a gank +%10 (uyumla ölçekli).
        if (lane == Lane.Bot && Tactics(side).BotGankSuccessBonus > 0)
            success += Tactics(side).BotGankSuccessBonus;
        if (JungleRng.NextDouble() < success)
        {
            _scores.Add(ScoreCategory.Jungle, side.Sign() * (withSupport ? GankWinValueWithSupport : GankWinValueSolo));
            var text = withSupport
                ? $"{jungler.Name} ve {support.Name} {LaneName(lane)} koridora baskın yaptı, {victim.Name} düştü"
                : $"{jungler.Name} {LaneName(lane)} koridora gank attı, {victim.Name} düştü";
            Kill(side, jungler, victim, t, text, lane, assists: withSupport ? [support] : []);
        }
        else
        {
            _scores.Add(ScoreCategory.Jungle, -side.Sign() * GankFailValue);
            var who = withSupport ? $"{jungler.Name} ve {support.Name}" : jungler.Name;
            _events.Add(new GankFailEvent(t, $"{who} {LaneName(lane)} koridorda boşa gitti", side, lane));
        }
    }
}
