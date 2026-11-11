namespace LolDraft.Engine.Simulation;

/// <summary>Kuralların etkili olduğu savaş türü. Objektif, kuşatma ve son savaş takım savaşıdır.</summary>
internal enum FightKind
{
    Objective,
    Siege,
    Final,
    Skirmish,
}

/// <summary>
/// Takım kuralları K5-K11, K13 (DESIGN.md §16.8): draft'ın kimliği, savaşın d'sine eklenir (Mavi lehine pozitif).
/// Her kural sadece kendi savaş türünde işler. Saf: şampiyonlara, dakikaya ve verilen güce bakar, zar atmaz.
/// </summary>
internal static class TeamRules
{
    // K5 Engage (objektif, son savaş): etkin engage = en toplamı − rakibin (dis + peel) toplamının yarısı (≥ 0).
    // d += 1,0 × fark (±4). Örnek: erken comp en 7 − 3,5 = 3,5, scale comp en 3 − 1,5 = 1,5 → Mavi +2.
    private const double EngageWeight = 1.0;
    private const double EngageCap = 4;
    private const double EngageCounterShare = 1.0;

    // K6 Poke: kuşatmada d += 1,0 × poke farkı (±4). Objektif savaşında rakip en toplamı ≥ 8 ise poke'u fazla olana −2
    // (sert engage üstüne atlar, poke'a vakit kalmaz). 8 ≈ havuzda beklenen takım en toplamı (5) × 1,6: iki tank + bir
    // engage'çi. Eskiden 5'ti: ortalama takımı da cezalandırıyordu, Poke kimliği %42 kazanıyordu (DESIGN.md faz D).
    // Sert engage yoksa objektifte poke farkı yarım ağırlıkla (±2): "engage'i olmayanı objektif öncesi erit".
    private const double PokeWeight = 1.0;
    private const double PokeCap = 4;
    private const int HardEngage = 8;
    private const double PokeObjectiveWeight = 0.5;
    private const double PokeObjectiveCap = 2;
    private const double PokeEngagedPenalty = 2;

    // K7 Pick (nehir çatışması): gelenlerin pick farkı, d += 1,0 × fark (±3).
    // Gank: başarı += %5 × (gank'a gelenlerin pick toplamı − hedefin mob'u), ±%15.
    private const double PickWeight = 1.0;
    private const double PickCap = 3;
    private const double GankPickChance = 0.05;
    private const double GankPickCap = 0.15;

    // K8 Dive tehdidi (takım savaşı): rakipte hiper taşıyıcı var ve rakip peel + fl < 3 → d += 0,8 × dive toplamı (±3).
    private const double DiveWeight = 0.8;
    private const double DiveCap = 3;
    private const int DiveProtection = 3;

    // K9 Korunan taşıyıcı (dk ≥ 25 takım savaşı): hiper taşıyıcısı olan takımda peel ≥ 3 → d +2;
    // peel + fl < 2 → taşıyıcının gücü × 0,85 (d'den 0,15 × G_eff düşer).
    private const int CarryMinute = 25;
    private const int PeelForCarry = 3;
    private const double ProtectedCarryBonus = 2;
    private const int UnprotectedThreshold = 2;
    private const double UnprotectedCarryLoss = 0.15;

    // K10 Frontline eksik (takım savaşı): fl toplamı < 2 → d −3; rakip en ≥ 4 ise −4.
    private const int FrontlineNeeded = 2;
    private const double NoFrontlinePenalty = 3;
    private const double NoFrontlineVsEngagePenalty = 4;
    private const int EngageVsNoFrontline = 4;

    // K11 Tek tip hasar (takım savaşı): ortalama AP payı < %20 ya da > %80 → d −2 (rakip tek tip savunma dizer).
    private const double MinApShare = 0.2;
    private const double MaxApShare = 0.8;
    private const double OneDamageTypePenalty = 2;

    // K13 Kombo (takım savaşı): iki şampiyon aynı takımdaysa d +2. Havuz: data/champions.json.
    private const double ComboBonus = 2;

    private static readonly (string A, string B)[] Combos =
    [
        ("kayle", "lulu"),      // Lulu koruması + hiper taşıyıcı
        ("jinx", "lulu"),
        ("vayne", "lulu"),
        ("leona", "draven"),    // Leona engage + Draven hasarı
        ("yasuo", "malphite"),  // knock-up → Yasuo ultisi
        ("yasuo", "alistar"),
        ("orianna", "amumu"),   // Amumu ultisi + Orianna shockwave (wombo)
    ];

    /// <summary>
    /// Savaşa giren iki taraf (takım savaşında tüm takım, çatışmada gelenler) için kural etkileri, kural sırasıyla.
    /// <paramref name="power"/>: şampiyonun o anki gerçek gücü (K9 korumasız taşıyıcı için).
    /// </summary>
    public static IReadOnlyList<RuleEffect> Effects(
        FightKind kind, IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, int minute,
        Func<ChampionProfile, double> power)
    {
        var effects = new List<RuleEffect>();
        if (kind == FightKind.Skirmish)
        {
            Difference("K7", blue, red, c => c.Tags.Pick, PickWeight, PickCap, name => $"Pick üstünlüğü ({name})", effects);
            return effects;
        }

        if (kind is FightKind.Objective or FightKind.Final)
            Engage(blue, red, effects);
        if (kind == FightKind.Siege)
            Difference("K6", blue, red, c => c.Tags.Poke, PokeWeight, PokeCap, name => $"Poke üstünlüğü ({name})", effects);
        if (kind == FightKind.Objective)
            PokeEngaged(blue, red, effects);

        foreach (var (side, us, them) in new[] { (TeamSide.Blue, blue, red), (TeamSide.Red, red, blue) })
        {
            DiveThreat(side, us, them, effects);
            if (minute >= CarryMinute)
                ProtectedCarry(side, us, power, effects);
            NoFrontline(side, us, them, effects);
            OneDamageType(side, us, effects);
            Combo(side, us, effects);
        }

        return effects;
    }

    /// <summary>K7 gank: başarı ihtimaline eklenen pay (±%15). Karne metni: <see cref="GankText"/>.</summary>
    public static double GankPickBonus(IEnumerable<ChampionProfile> gankers, ChampionProfile victim) =>
        Math.Clamp(GankPickChance * (gankers.Sum(c => c.Tags.Pick) - victim.Tags.Mobility), -GankPickCap, GankPickCap);

    public static string GankText(ChampionProfile jungler) => $"Gank'ta yakalama ({jungler.Name})";

    // Fark kuralı: fark kimin lehineyse ona, ağırlık × fark (tavanlı). Kaynak: o takımda etiketi en yüksek şampiyon.
    private static void Difference(
        string rule, IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, Func<ChampionProfile, int> tag,
        double weight, double cap, Func<string, string> text, List<RuleEffect> effects)
    {
        var diff = blue.Sum(tag) - red.Sum(tag);
        if (diff == 0)
            return;

        var (side, team) = diff > 0 ? (TeamSide.Blue, blue) : (TeamSide.Red, red);
        var source = team.MaxBy(tag)!;
        effects.Add(new RuleEffect(rule, side, Math.Min(weight * Math.Abs(diff), cap), source, text(source.Name)));
    }

    private static void Engage(IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, List<RuleEffect> effects)
    {
        var diff = EffectiveEngage(blue, red) - EffectiveEngage(red, blue);
        if (diff == 0)
            return;

        var (side, team) = diff > 0 ? (TeamSide.Blue, blue) : (TeamSide.Red, red);
        var source = team.MaxBy(c => c.Tags.Engage)!;
        effects.Add(new RuleEffect("K5", side, Math.Min(EngageWeight * Math.Abs(diff), EngageCap), source,
            $"Engage üstünlüğü ({source.Name})"));
    }

    private static double EffectiveEngage(IReadOnlyList<ChampionProfile> us, IReadOnlyList<ChampionProfile> them) =>
        Math.Max(0, us.Sum(c => c.Tags.Engage) - EngageCounterShare * them.Sum(c => c.Tags.Disengage + c.Tags.Peel));

    private static void PokeEngaged(IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, List<RuleEffect> effects)
    {
        var diff = blue.Sum(c => c.Tags.Poke) - red.Sum(c => c.Tags.Poke);
        if (diff == 0)
            return;

        var (side, poke, engage) = diff > 0 ? (TeamSide.Blue, blue, red) : (TeamSide.Red, red, blue);
        var source = poke.MaxBy(c => c.Tags.Poke)!;
        if (engage.Sum(c => c.Tags.Engage) >= HardEngage)
        {
            effects.Add(new RuleEffect("K6", side, -PokeEngagedPenalty, source, $"Poke, engage'e yakalandı ({source.Name})"));
            return;
        }

        // Sert engage yoksa poke objektifi kurar: rakibi objektif öncesi aşındırır.
        effects.Add(new RuleEffect("K6", side, Math.Min(PokeObjectiveWeight * Math.Abs(diff), PokeObjectiveCap), source,
            $"Poke objektifi kurdu ({source.Name})"));
    }

    private static void DiveThreat(TeamSide side, IReadOnlyList<ChampionProfile> us, IReadOnlyList<ChampionProfile> them, List<RuleEffect> effects)
    {
        var dive = us.Sum(c => c.Tags.Dive);
        if (dive == 0 || !them.Any(c => c.IsHyperCarry) || them.Sum(c => c.Tags.Peel + c.Tags.Frontline) >= DiveProtection)
            return;

        var source = us.MaxBy(c => c.Tags.Dive)!;
        effects.Add(new RuleEffect("K8", side, Math.Min(DiveWeight * dive, DiveCap), source, $"Taşıyıcıya dive ({source.Name})"));
    }

    private static void ProtectedCarry(TeamSide side, IReadOnlyList<ChampionProfile> us, Func<ChampionProfile, double> power, List<RuleEffect> effects)
    {
        var carries = us.Where(c => c.IsHyperCarry).ToList();
        if (carries.Count == 0)
            return;

        if (us.Sum(c => c.Tags.Peel) >= PeelForCarry)
        {
            effects.Add(new RuleEffect("K9", side, ProtectedCarryBonus, carries[0], $"Korunan taşıyıcı ({carries[0].Name})"));
            return;
        }

        if (us.Sum(c => c.Tags.Peel + c.Tags.Frontline) < UnprotectedThreshold)
        {
            foreach (var carry in carries)
                effects.Add(new RuleEffect("K9", side, -UnprotectedCarryLoss * power(carry), carry, $"Korunmasız taşıyıcı ({carry.Name})"));
        }
    }

    private static void NoFrontline(TeamSide side, IReadOnlyList<ChampionProfile> us, IReadOnlyList<ChampionProfile> them, List<RuleEffect> effects)
    {
        if (us.Sum(c => c.Tags.Frontline) >= FrontlineNeeded)
            return;

        var penalty = them.Sum(c => c.Tags.Engage) >= EngageVsNoFrontline ? NoFrontlineVsEngagePenalty : NoFrontlinePenalty;
        effects.Add(new RuleEffect("K10", side, -penalty, us.MaxBy(c => c.Tags.Frontline)!, "Frontline eksik"));
    }

    private static void OneDamageType(TeamSide side, IReadOnlyList<ChampionProfile> us, List<RuleEffect> effects)
    {
        var apShare = us.Average(c => c.ApShare);
        if (apShare is < MinApShare or > MaxApShare)
        {
            effects.Add(new RuleEffect("K11", side, -OneDamageTypePenalty, us.MaxBy(c => Math.Abs(c.ApShare - 0.5))!,
                apShare < MinApShare ? "Tek tip hasar (hep AD)" : "Tek tip hasar (hep AP)"));
        }
    }

    private static void Combo(TeamSide side, IReadOnlyList<ChampionProfile> us, List<RuleEffect> effects)
    {
        foreach (var (a, b) in Combos)
        {
            var first = us.FirstOrDefault(c => c.Id == a);
            var second = us.FirstOrDefault(c => c.Id == b);
            if (first is not null && second is not null)
                effects.Add(new RuleEffect("K13", side, ComboBonus, first, $"Kombo: {first.Name} + {second.Name}"));
        }
    }
}
