using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Koridor akışı, sürpriz ve anlatıcı (DESIGN.md §4.5, §4.8).</summary>
internal sealed partial class MatchRun
{
    // Koridor akışı ayarları (DESIGN.md §4.5, §9 ayar düğmeleri).
    private const double LanePowerFactor = 0.1;
    private const double LaneNoiseSigma = 0.8;

    // Sürpriz: her koridorda her dakika %2 ihtimalle rastgele bir taraf 3-5 puan kazanır.
    private const double SurpriseChance = 0.02;

    // Anlatıcı eşikleri ve bütçeden düşülen bedeller (DESIGN.md §4.8).
    private const double SoloKillCost = 8;
    private const double TowerCost = 10;
    private const double PlateCost = 5;
    private const double CsCost = 3;
    private const int TowerMinMinute = 14;
    private const int PlateMinMinute = 5;
    private const int CsCooldownMinutes = 4;
    private const int TowersPerLane = 3;

    private static readonly Lane[] Lanes = [Lane.Top, Lane.Mid, Lane.Bot];

    // Anlatıcı bütçesi: koridorda birikmiş, henüz olaya dönüşmemiş üstünlük. Pozitif = Mavi önde.
    private readonly double[] _laneBudget = new double[Lanes.Length];
    // -9: maç başında "son CS olayı çok önceydi" (JS: lastCs = -9).
    private readonly int[] _lastCsMinute = [-9, -9, -9];
    // [taraf, koridor]: o tarafın o koridorda yıktığı kule sayısı (0 dış, 1 iç, 2 inhibitör).
    private readonly int[,] _towersDestroyed = new int[2, Lanes.Length];

    private void PlayLanes(int minute)
    {
        var t0 = (minute - 1) * 60;

        foreach (var lane in Lanes)
        {
            // Δ = 0.1 × (G_koridor_Mavi − G_koridor_Kırmızı) + matchup[koridor] + N(0, 0.8)
            // Güç farkı akışın yönünü belirler. Gürültü tek bir dakikayı çevirebilir ama 35 dakikada güç farkı birikir.
            var delta = LanePowerFactor * (LanePower(_blue, lane, minute) - LanePower(_red, lane, minute))
                        + Matchup(lane)
                        + _rng.NextNormal() * LaneNoiseSigma;

            // Δ hem puana hem anlatıcı bütçesine yazılır; puan burada eklenir, anlatıcı sadece anlatır.
            _scores.Add(LaneCategory(lane), delta);
            _laneBudget[(int)lane] += delta;

            if (_rng.NextDouble() < SurpriseChance)
                PlaySurprise(lane, t0);

            Narrate(lane, minute, t0);
        }
    }

    // Sürpriz gerçek puandır (Surprise kategorisi); bütçeye girmez, kendi olayını kendisi üretir.
    // Zar sırası: taraf, değer, metin, saniye.
    private void PlaySurprise(Lane lane, int t0)
    {
        var side = _rng.NextDouble() < 0.5 ? TeamSide.Blue : TeamSide.Red;
        var value = 3 + _rng.NextDouble() * 2;
        var hero = LaneChampions(side, lane)[0];
        var rival = LaneChampions(side.Opponent(), lane)[0];
        _scores.Add(ScoreCategory.Surprise, side.Sign() * value);

        string[] texts =
        [
            $"{hero.Name} beklenmedik bir outplay yaptı",
            $"{rival.Name} riskli bir dive denedi ve geri püskürtüldü",
            $"{hero.Name} rakibin ışınlanmasını boşa çıkardı",
        ];
        var text = texts[_rng.NextIndex(texts.Length)];
        var t = t0 + 5 + _rng.NextDouble() * 20;

        _events.Add(new SurpriseEvent(t, $"Sürpriz: {text}", side, lane, value));
    }

    // Anlatıcı (DESIGN.md §4.8): bütçenin işaretine göre önde olan taraf için en fazla bir olay.
    // Şartlar sırayla denenir; ilk tutan olay üretilir ve bedeli bütçeden düşülür.
    // Saniye zarı her dakika atılır (olay olmasa da), zar sırası bu yüzden sabit kalır.
    private void Narrate(Lane lane, int minute, int t0)
    {
        ref var budget = ref _laneBudget[(int)lane];
        var side = budget >= 0 ? TeamSide.Blue : TeamSide.Red;
        var size = Math.Abs(budget);
        var t = t0 + 25 + _rng.NextDouble() * 30;

        var lead = LaneChampions(side, lane)[0];
        var rivals = LaneChampions(side.Opponent(), lane);
        ref var towers = ref _towersDestroyed[(int)side, (int)lane];
        ref var lastCs = ref _lastCsMinute[(int)lane];

        if (size >= SoloKillCost && _deaths.IsAlive(lead, t) && rivals.Any(c => _deaths.IsAlive(c, t)))
        {
            var alive = rivals.Where(c => _deaths.IsAlive(c, t)).ToList();
            var victim = alive[_rng.NextIndex(alive.Count)];
            Kill(side, lead, victim, t, $"{lead.Name} {LaneName(lane)} koridorda öne çıktı, {victim.Name} düştü", lane);
            budget -= side.Sign() * SoloKillCost;
        }
        else if (minute >= TowerMinMinute && size >= TowerCost && towers < TowersPerLane)
        {
            var tier = towers++;
            budget -= side.Sign() * TowerCost;
            _events.Add(new TowerEvent(t, $"{side.DisplayName()}, {LaneName(lane)} koridorda {TowerName(tier)} yıktı", side, lane, tier));
        }
        else if (minute >= PlateMinMinute && minute < TowerMinMinute && size >= PlateCost)
        {
            budget -= side.Sign() * PlateCost;
            _events.Add(new PlateEvent(t, $"{lead.Name} {LaneName(lane)} kuleden plaka kopardı", side, lane));
        }
        else if (size >= CsCost && minute - lastCs >= CsCooldownMinutes)
        {
            lastCs = minute;
            budget -= side.Sign() * CsCost;
            _events.Add(new CsEvent(t, $"{lead.Name} {LaneName(lane)} koridorda CS farkı açıyor", side, lane));
        }
    }

    // Koridordaki şampiyonlar; ilk sıradaki koridorun "yüzü" (bot'ta adc).
    private static ChampionProfile[] LaneChampions(TeamDraft team, Lane lane) => lane switch
    {
        Lane.Top => [team.Top],
        Lane.Mid => [team.Mid],
        Lane.Bot => [team.Bot, team.Support],
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };

    private ChampionProfile[] LaneChampions(TeamSide side, Lane lane) => LaneChampions(Team(side), lane);

    // Koridor gücü: koridordaki şampiyonların G ortalaması; bot = (adc + sup) / 2 (DESIGN.md §4.3).
    private static double LanePower(TeamDraft team, Lane lane, int minute) =>
        LaneChampions(team, lane).Sum(c => c.PowerAt(minute)) / (double)LaneChampions(team, lane).Length;

    // Prototipteki sabit matchup; ileride veriden gelecek (DESIGN.md §3).
    private static double Matchup(Lane lane) => lane switch
    {
        Lane.Top => 0.1,
        Lane.Mid => 0.05,
        _ => 0,
    };

    private static ScoreCategory LaneCategory(Lane lane) => lane switch
    {
        Lane.Top => ScoreCategory.Top,
        Lane.Mid => ScoreCategory.Mid,
        Lane.Bot => ScoreCategory.Bot,
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };

    private static string LaneName(Lane lane) => lane switch
    {
        Lane.Top => "üst",
        Lane.Mid => "orta",
        Lane.Bot => "alt",
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };

    private static string TowerName(int tier) => tier switch
    {
        0 => "dış kuleyi",
        1 => "iç kuleyi",
        2 => "inhibitörü",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
