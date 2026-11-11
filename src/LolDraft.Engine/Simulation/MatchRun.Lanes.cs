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

    // Sürpriz bir kill'dir; ilk dakikalarda koridor yok (minyonlar ~1:05'te varır, ilk gank 3. dk). Zar her dakika
    // atılır (zar sırası kaymasın), 3. dakikadan önce uygulanmaz.
    private const int SurpriseMinMinute = 3;

    // Anlatıcı eşikleri ve bütçeden düşülen bedeller (DESIGN.md §4.8). Kule kuralı MatchRun.Towers.cs'te.
    private const double SoloKillCost = 8;
    private const double PlateCost = 5;
    private const int PlateMinMinute = 5;

    // CS anlatımı gerçek CS farkına bakar (bütçeye değil): koridorun yüzleri arasında ≥ 15 CS, 4 dk bekleme.
    private const int CsGapToNarrate = 15;
    private const int CsCooldownMinutes = 4;

    private static readonly Lane[] Lanes = [Lane.Top, Lane.Mid, Lane.Bot];

    // Anlatıcı bütçesi: koridorda birikmiş, henüz olaya dönüşmemiş üstünlük. Pozitif = Mavi önde.
    private readonly double[] _laneBudget = new double[Lanes.Length];
    // -9: maç başında "son CS olayı çok önceydi" (JS: lastCs = -9).
    private readonly int[] _lastCsMinute = [-9, -9, -9];

    // Koridor akışı dakika başında puana yazılır; sürpriz (5-25. sn) ve anlatıcı (25-55. sn) kendi saniyelerine kaydolur.
    private void ScheduleLanes(int minute)
    {
        var t0 = (minute - 1) * 60;

        foreach (var lane in Lanes)
        {
            // Δ = 0.1 × (G_eff_koridor_Mavi − G_eff_koridor_Kırmızı) + koridor kuralları + N(0, 0.8)   (DESIGN.md §16.6)
            // Gerçek güç farkı akışın yönünü belirler. Gürültü tek bir dakikayı çevirebilir ama güç farkı birikir.
            // Kurallar (K1, K3, K4) draft'tan gelen koridor avantajı; K2 (tank tamponu) tüm Δ'yı yumuşatır.
            // Koridor içi kartopu: Δ → CS → altın → G_eff → Δ. Taban/tavan (0,5-1,4) ve ödül dengeler.
            var blue = LaneChampions(TeamSide.Blue, lane);
            var red = LaneChampions(TeamSide.Red, lane);
            var rules = LaneRules.Effects(lane, blue, red, minute);
            var rawDelta = LanePowerFactor * (LanePower(TeamSide.Blue, lane, minute, t0) - LanePower(TeamSide.Red, lane, minute, t0))
                           + rules.Sum(e => e.Signed)
                           + _rng.NextNormal() * LaneNoiseSigma;
            var (factor, damping) = LaneRules.Damping(lane, blue, red, minute);
            var delta = rawDelta * factor;
            ReportLaneRules(lane, rules, rawDelta, factor, damping);

            // Δ hem puana hem anlatıcı bütçesine yazılır; puan burada eklenir, anlatıcı sadece anlatır.
            _scores.Add(LaneCategory(lane), delta);
            _laneBudget[(int)lane] += delta;
            _laneDelta[(int)lane] = delta;

            if (_rng.NextDouble() < SurpriseChance && minute >= SurpriseMinMinute)
            {
                var surpriseT = t0 + 5 + _rng.NextDouble() * 20;
                At(surpriseT, () => PlaySurprise(lane, surpriseT));
            }

            var narrateT = t0 + 25 + _rng.NextDouble() * 30;
            At(narrateT, () => Narrate(lane, minute, narrateT));
        }
    }

    // Sürpriz (DESIGN.md §4.5): koridorda beklenmedik bir an, kill'e döner. Kendi ödülünü icat etmez; altın, ödül,
    // respawn ve kaçan farm kill kurallarından gelir. Önde olanı indiren onun ödülünü toplar: geri dönüş anı.
    // İki tür, eşit ihtimal; ikisinde de kill sürprizi yapan tarafın:
    //   outplay: kahraman rakibini indirir · dive: rakip kahramanın üstüne atlar, geri püskürtülür ve düşer.
    // Kahraman (koridorun yüzü, yoksa bot'taki diğeri) ya da rakiplerin hepsi koridorda değilse (ölü, savaşta, gank'ta)
    // sürpriz olmaz: anlatıcı kill'lerindeki kuralın aynısı.
    // Örnek: 12. dk Zed, LeBlanc'tan 1.200 önde; LeBlanc'ın sürprizi → 300 + ~430 ödül, fark kapanır.
    // Puan (Surprise kategorisi) sadece dökümde (karara girmez). Zar sırası: taraf, değer, tür, kurban.
    private void PlaySurprise(Lane lane, double t)
    {
        var side = _rng.NextDouble() < 0.5 ? TeamSide.Blue : TeamSide.Red;
        var hero = LaneChampions(side, lane).FirstOrDefault(c => IsInLane(c, t));
        var rivals = LaneChampions(side.Opponent(), lane).Where(c => IsInLane(c, t)).ToList();
        if (hero is null || rivals.Count == 0)
            return;

        var value = 3 + _rng.NextDouble() * 2;
        var outplay = _rng.NextDouble() < 0.5;
        var victim = rivals[_rng.NextIndex(rivals.Count)];
        _scores.Add(ScoreCategory.Surprise, side.Sign() * value);

        var text = outplay
            ? $"Sürpriz: {hero.Name} beklenmedik bir outplay yaptı, {victim.Name} düştü"
            : $"Sürpriz: {victim.Name} riskli bir dive denedi, {hero.Name} onu indirdi";

        // Metinsiz işaret (haritada "Sürpriz" rozeti); log satırı kill olayının kendisi.
        _events.Add(new SurpriseEvent(t, "", side, lane, value));
        Kill(side, hero, victim, t, text, lane);
    }

    // Anlatıcı (DESIGN.md §4.8): bütçenin işaretine göre önde olan taraf için en fazla bir olay.
    // Şartlar sırayla denenir; ilk tutan olay üretilir. Solo kill ve plaka bütçeden bedel düşer (sadece anlatım);
    // kule ise fırsattır (rakip yok) ve kendi puanını getirir (MatchRun.Towers.cs). İkisi de olmadıysa CS anlatımı
    // gerçek CS farkına bakar.
    // Saniye her dakika kayıtta seçilir (olay olmasa da); çözüm o saniyedeki duruma bakar.
    private void Narrate(Lane lane, int minute, double t)
    {
        ref var budget = ref _laneBudget[(int)lane];
        var side = budget >= 0 ? TeamSide.Blue : TeamSide.Red;
        var size = Math.Abs(budget);

        var lead = LaneChampions(side, lane)[0];
        var rivals = LaneChampions(side.Opponent(), lane);

        if (TryLaneTower(side, lane, minute, t))
        {
            // Kule düştü; bu dakika bu koridorda başka anlatım yok.
            return;
        }

        // Anlatıcı koridorda olanı anlatır: kahraman ölü ya da başka yerde meşgulse kill/plaka yok.
        // Bütçe harcanmaz, birikmeye devam eder; kahraman dönünce anlatılır (puan zaten Δ ile eklendi).
        if (IsInLane(lead, t))
        {
            var rivalsInLane = rivals.Where(c => IsInLane(c, t)).ToList();
            if (size >= SoloKillCost && rivalsInLane.Count > 0)
            {
                var victim = rivalsInLane[_rng.NextIndex(rivalsInLane.Count)];
                Kill(side, lead, victim, t, $"{lead.Name} {LaneName(lane)} koridorda öne çıktı, {victim.Name} düştü", lane);
                budget -= side.Sign() * SoloKillCost;
                return;
            }

            if (minute >= PlateMinMinute && minute < TowerMinMinute && size >= PlateCost)
            {
                budget -= side.Sign() * PlateCost;
                _gold.Earn(lead, GoldSource.Plate, PlateGold);
                _events.Add(new PlateEvent(t, $"{lead.Name} {LaneName(lane)} kuleden plaka kopardı", side, lane));
                return;
            }
        }

        TryNarrateCs(lane, minute, t);
    }

    // CS anlatımı: koridorun yüzleri (bot'ta adc) arasında ≥ 15 CS fark varsa, öndeki koridordaysa, 4 dk'da bir.
    private void TryNarrateCs(Lane lane, int minute, double t)
    {
        ref var lastCs = ref _lastCsMinute[(int)lane];
        var blue = LaneChampions(TeamSide.Blue, lane)[0];
        var red = LaneChampions(TeamSide.Red, lane)[0];
        var blueCs = (int)Math.Floor(_gold.Cs(blue));
        var redCs = (int)Math.Floor(_gold.Cs(red));
        var side = blueCs >= redCs ? TeamSide.Blue : TeamSide.Red;
        var lead = side == TeamSide.Blue ? blue : red;

        if (Math.Abs(blueCs - redCs) < CsGapToNarrate || minute - lastCs < CsCooldownMinutes || !IsInLane(lead, t))
            return;

        lastCs = minute;
        var (leadCs, rivalCs) = side == TeamSide.Blue ? (blueCs, redCs) : (redCs, blueCs);
        _events.Add(new CsEvent(t, $"{lead.Name} {LaneName(lane)} koridorda CS farkı açıyor ({leadCs} - {rivalCs})", side, lane));
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

    // Koridor gücü: koridordaki şampiyonların G_eff ortalaması; bot = (adc + sup) / 2.
    private double LanePower(TeamSide side, Lane lane, int minute, double t)
    {
        var laners = LaneChampions(side, lane);
        return EffectivePower(laners, minute, t) / laners.Length;
    }

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
}
