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
/// Takım kuralları K5-K11, K13, K14 (DESIGN.md §16.8): draft'ın kimliği, savaşın d'sine eklenir (Mavi lehine pozitif).
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

    // K6 poke itişi (koridor kulesi, dk ≥ 14): poke'u fazla olan takım kuleyi savunan koridordayken de alabilir
    // (savunanı kule altında aşındırır). Normal fırsat yoksa her koridor anında ihtimal %1 × poke farkı (en fazla %5).
    // Faz D tur 4: Poke'un frontline'sız kazanma yolu kule → kuşatma.
    private const double PokePushPerPoke = 0.01;
    private const double PokePushCap = 0.05;

    // K7 Pick (nehir çatışması): gelenlerin pick farkı, d += 1,0 × fark (±3).
    // Gank: başarı += %5 × (gank'a gelenlerin pick toplamı − hedefin mob'u), ±%15.
    private const double PickWeight = 1.0;
    private const double PickCap = 3;
    private const double GankPickChance = 0.05;
    private const double GankPickCap = 0.15;

    // K8 Dive (takım savaşı): etkin dive = dive toplamı − rakibin peel toplamı (≥ 0); iki takımın etkin dive farkı
    // fazla olana d += 0,5 × fark (±3). Faz D tur 4: eskiden sadece rakipte hiper taşıyıcı ve peel + fl < 3 iken.
    private const double DiveWeight = 0.5;
    private const double DiveCap = 3;

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

    // K14 Toplu hasar (takım savaşı): etkin aoe = aoe toplamı, takımın en toplamı ≥ 5 ise × 1,5 (wombo: engage rakibi
    // toplar, toplu hasar vurur; 5 ≈ havuzdaki ortalama takım en'i). Etkin farkı fazla olana d += 0,25 × fark (±2).
    // Örnek: Leona (en 3) + Sejuani (en 3, aoe 1) + Brand (aoe 3) → 4 × 1,5 = 6; rakip aoe 2 → 0,25 × 4 = +1.
    // Faz D tur 5: 0,5 / ±3 Brand'i %46'ya çıkarıyordu ama Dive %42, erken comp %44,6 (aoe'liler çoğunlukla geç
    // oyun büyücü/nişancıları); yumuşak ayar seçildi (kullanıcı).
    private const double AreaWeight = 0.25;
    private const double AreaCap = 2;
    private const int WomboEngage = 5;
    private const double WomboFactor = 1.5;

    // Toplam tavan (takım savaşı): iki tarafın kural etkilerinin net toplamı ±8. Kurallar tek tek tavanlı ama üst üste
    // binince (K5 +4, K10 +4, K8 +3, K11 +2...) uç draft'lar %76-83 kazanıyordu (faz D tur 4). Normal draft'lara dokunmaz.
    private const double TotalCap = 8;

    // K13 Kombo (takım savaşı): iki şampiyon aynı takımdaysa d +2. Havuz: data/champions.json.
    private const double ComboBonus = 2;

    internal static readonly (string A, string B)[] Combos =
    [
        ("kayle", "lulu"),      // Lulu koruması + hiper taşıyıcı
        ("jinx", "lulu"),
        ("vayne", "lulu"),
        ("leona", "draven"),    // Leona engage + Draven hasarı
        ("yasuo", "malphite"),  // knock-up → Yasuo ultisi
        ("yasuo", "alistar"),
        ("orianna", "amumu"),   // Amumu ultisi + Orianna shockwave (wombo)
        // Faz H4 (80'lik havuz):
        ("xayah", "rakan"),     // birlikte tasarlanmış ikili: ortak geri çağırma, atılma
        ("yasuo", "yone"),      // Yone Q3 knock-up → Yasuo ultisi (Yone'nin ultisi knock-up istemez)
        ("yasuo", "wukong"),    // Wukong ultisi iki kez havaya kaldırır
        ("missfortune", "amumu"), // Amumu sabitler, Bullet Time hepsini vurur (wombo)
        ("kaisa", "nautilus"),  // Nautilus sabitler, Kai'Sa ultisiyle üstüne atlar
        ("lucian", "braum"),    // Lucian'ın çift atışı Braum pasifini hızla doldurur
        ("pyke", "draven"),     // kill odaklı bot lane (ikisi de bilinçli bandın altında, hafif destek)
        ("kalista", "blitzcrank"), // Kalista ultisiyle fırlatılan Blitzcrank kancası
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

        Dive(blue, red, effects);

        foreach (var (side, us, them) in new[] { (TeamSide.Blue, blue, red), (TeamSide.Red, red, blue) })
        {
            if (minute >= CarryMinute)
                ProtectedCarry(side, us, power, effects);
            NoFrontline(side, us, them, effects);
            OneDamageType(side, us, effects);
            Combo(side, us, effects);
        }

        AreaDamage(blue, red, effects);
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

    private static void Dive(IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, List<RuleEffect> effects)
    {
        var diff = EffectiveDive(blue, red) - EffectiveDive(red, blue);
        if (diff == 0)
            return;

        var (side, team) = diff > 0 ? (TeamSide.Blue, blue) : (TeamSide.Red, red);
        var source = team.MaxBy(c => c.Tags.Dive)!;
        effects.Add(new RuleEffect("K8", side, Math.Min(DiveWeight * Math.Abs(diff), DiveCap), source,
            $"Arka hatta dive ({source.Name})"));
    }

    /// <summary>Takım savaşında d'ye giren kural toplamı: net etkiler ±<see cref="TotalCap"/> ile sınırlı.</summary>
    public static double CappedTotal(double signedSum) => Math.Clamp(signedSum, -TotalCap, TotalCap);

    /// <summary>K6 poke itişi: <paramref name="us"/> takımının koridor anında kuleyi zorla alma ihtimali (poke'u azsa 0).</summary>
    public static double PokePushChance(IReadOnlyList<ChampionProfile> us, IReadOnlyList<ChampionProfile> them) =>
        Math.Clamp(PokePushPerPoke * (us.Sum(c => c.Tags.Poke) - them.Sum(c => c.Tags.Poke)), 0, PokePushCap);

    private static void AreaDamage(IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, List<RuleEffect> effects)
    {
        var diff = EffectiveArea(blue) - EffectiveArea(red);
        if (diff == 0)
            return;

        var (side, team) = diff > 0 ? (TeamSide.Blue, blue) : (TeamSide.Red, red);
        var source = team.MaxBy(c => c.Tags.AreaDamage)!;
        var wombo = team.Sum(c => c.Tags.Engage) >= WomboEngage;
        effects.Add(new RuleEffect("K14", side, Math.Min(AreaWeight * Math.Abs(diff), AreaCap), source,
            wombo ? $"Wombo: engage toplar, toplu hasar vurur ({source.Name})" : $"Toplu hasar üstünlüğü ({source.Name})"));
    }

    // Etkin toplu hasar: engage'i yüksek takımda × 1,5 (rakip bir araya toplanmış olur).
    private static double EffectiveArea(IReadOnlyList<ChampionProfile> team) =>
        team.Sum(c => c.Tags.AreaDamage) * (team.Sum(c => c.Tags.Engage) >= WomboEngage ? WomboFactor : 1);

    // Etkin dive: dalış gücünden rakibin koruması düşülür (tank dive'ı durdurmaz, peel durdurur).
    private static int EffectiveDive(IReadOnlyList<ChampionProfile> us, IReadOnlyList<ChampionProfile> them) =>
        Math.Max(0, us.Sum(c => c.Tags.Dive) - them.Sum(c => c.Tags.Peel));

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
