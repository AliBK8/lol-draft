using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Nehir çatışması (DESIGN.md §4.5): dakika aralıklarında, iki taraf ayrı ayrı 1-3 kişiyle.</summary>
internal sealed partial class MatchRun
{
    private const double SkirmishChance = 0.75;
    private const int SkirmishMaxSide = 3;
    private const double SkirmishScoreFactor = 0.1;
    private const double SkirmishDivisor = 6;
    private const double SkirmishValue = 3;
    private const int SkirmishMaxKills = 2;
    private const int SkirmishMoveLead = 14;
    private const int SkirmishMoveTail = 4;

    // Çatışma aralıkları (dk, dahil). Her aralıkta en fazla bir çatışma.
    private static readonly (int From, int To)[] SkirmishWindows = [(7, 9), (12, 14), (17, 19), (25, 27)];

    // Aralığın başında seçilen çatışma dakikaları.
    private readonly HashSet<int> _skirmishMinutes = [];

    // Kimin çatışmaya geleceği: nehre en yakın roller önce (ormancı, support, mid, adc, top).
    private static ChampionProfile[] SkirmishPriority(TeamDraft team) =>
        [team.Jungle, team.Support, team.Mid, team.Bot, team.Top];

    // Aralığın ilk dakikasında: olacak mı (%75), olacaksa aralıktaki hangi dakika (eşit ihtimal).
    // Çatışma dakikasında: iki tarafın kişi sayısı (ayrı ayrı 1-3) ve saniye (15-30. sn) seçilir, çözüme kaydolur.
    private void ScheduleSkirmish(int minute)
    {
        foreach (var (from, to) in SkirmishWindows)
        {
            if (minute == from && _rng.NextDouble() < SkirmishChance)
                _skirmishMinutes.Add(from + _rng.NextIndex(to - from + 1));
        }

        if (!_skirmishMinutes.Contains(minute))
            return;

        var blueSize = 1 + _rng.NextIndex(SkirmishMaxSide);
        var redSize = 1 + _rng.NextIndex(SkirmishMaxSide);
        var t = (minute - 1) * 60 + 15 + _rng.NextDouble() * 15;
        At(t, () => PlaySkirmish(minute, blueSize, redSize, t));
    }

    private void PlaySkirmish(int minute, int blueSize, int redSize, double t)
    {
        // Her taraftan öncelik sırasıyla yaşayan ilk N kişi gelir; ölüler yüzünden daha az da gelebilir.
        var blue = SkirmishPriority(_blue).Where(c => _deaths.IsAlive(c, t)).Take(blueSize).ToList();
        var red = SkirmishPriority(_red).Where(c => _deaths.IsAlive(c, t)).Take(redSize).ToList();
        if (blue.Count == 0 || red.Count == 0)
            return;

        // Çatışma ihtimali (DESIGN.md §4.5):
        //   d      = ΣG_Mavi − ΣG_Kırmızı + 0.1 × toplam puan
        //   p_Mavi = clamp(1 / (1 + e^(−d/6)))
        // Bölen 6 (takım savaşında 15): az kişide güç farkı daha belirleyici. Kişi sayısı farkı doğrudan ΣG'ye
        // yansır: herkes G=6 iken 1v3'te fark −12 → ~%12 (kalabalığa yakalanmak).
        var d = blue.Sum(c => c.PowerAt(minute)) - red.Sum(c => c.PowerAt(minute)) + SkirmishScoreFactor * _scores.Total;
        var blueProbability = Probability.Clamp(Probability.Logistic(d / SkirmishDivisor));
        var winner = _rng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var winnerProbability = winner == TeamSide.Blue ? blueProbability : 1 - blueProbability;
        _scores.Add(ScoreCategory.Skirmish, winner.Sign() * SkirmishValue);
        // Çatışmaya gelenler bir süre koridorlarında değil: koridorları kule fırsatına açılır.
        MarkAway([.. blue, .. red], t + AwayAfterFightSeconds);

        var river = _rng.NextDouble() < 0.5 ? RiverSide.Top : RiverSide.Bottom;
        string[] parts = [.. blue.Select(c => c.Id), .. red.Select(c => c.Id)];
        _events.Add(new MoveEvent(t, "", parts, Lane: null, river, SkirmishMoveLead, SkirmishMoveTail));
        _events.Add(new SkirmishEvent(t, $"Nehirde {blue.Count}v{red.Count} çatışma, {winner.DisplayName()} kazandı",
            winner, winnerProbability, river, blue.Count, red.Count));

        // Kaybedenlerden 1-2 kill (kaybeden tek kişiyse 1). Kill'ler 1.5 sn arayla t+1'den başlar.
        var winners = winner == TeamSide.Blue ? blue : red;
        var losers = winner == TeamSide.Blue ? red : blue;
        var killCount = 1 + _rng.NextIndex(Math.Min(SkirmishMaxKills, losers.Count));
        ScheduleKills(winner, winners, losers, killCount, t + 1,
            (killer, victim) => $"{killer.Name}, {victim.Name} karakterini nehirde yakaladı");
    }
}
