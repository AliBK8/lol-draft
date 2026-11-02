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
    private const int GankSecond = 40;
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

    private static readonly HashSet<int> GankMinutes = [3, 6, 9, 12, 18, 24];

    // Orman akışı dakika başında puana yazılır; gank 40. saniyeye kaydolur.
    private void ScheduleJungle(int minute)
    {
        var t0 = (minute - 1) * 60;
        var powerDiff = EffectivePower(_blue.Jungle, minute, t0) - EffectivePower(_red.Jungle, minute, t0);

        // orman += 0.05 × (G_eff_jg_Mavi − G_eff_jg_Kırmızı) + N(0, 0.3)
        _jungleDelta = JunglePowerFactor * powerDiff + _rng.NextNormal() * JungleNoiseSigma;
        _scores.Add(ScoreCategory.Jungle, _jungleDelta);

        if (GankMinutes.Contains(minute))
            At((minute - 1) * 60 + GankSecond, () => PlayGank(minute, powerDiff));
    }

    // Zar sırası: taraf, koridor, (şartlar tutarsa) support katılımı, başarı.
    private void PlayGank(int minute, double powerDiff)
    {
        // Gank'ı atan taraf: p_Mavi = clamp(1 / (1 + e^(−(G_eff_jg farkı)/4))). Eşit ormancıda %50.
        // Prototipte sıkıştırılmıyordu (fark 9'da ~%90.5); her olay ihtimali gibi %10-%90.
        var blueProbability = Probability.Clamp(Probability.Logistic(powerDiff / GankSideDivisor));
        var side = _rng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var lane = Lanes[_rng.NextIndex(Lanes.Length)];
        var team = Team(side);
        var jungler = team.Jungle;
        var support = team.Support;
        var t = (minute - 1) * 60 + GankSecond;

        // Koridorda yaşayan ilk rakip hedeftir; kimse yoksa ya da ormancı ölüyse gank olmaz.
        var victim = LaneChampions(side.Opponent(), lane).FirstOrDefault(c => _deaths.IsAlive(c, t));
        if (victim is null || !_deaths.IsAlive(jungler, t))
            return;

        // Support sadece lane fazında ve bot dışındaki koridora katılır (bot'ta zaten orada).
        // Kısa devre sırası önemli: önceki şartlardan biri tutmazsa zar atılmaz (JS && ile aynı).
        var withSupport = minute < SupportRoamBeforeMinute
                          && lane != Lane.Bot
                          && _deaths.IsAlive(support, t)
                          && _rng.NextDouble() < SupportRoamChance;

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
        if (_rng.NextDouble() < success)
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
