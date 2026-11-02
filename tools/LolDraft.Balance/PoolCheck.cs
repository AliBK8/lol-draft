using LolDraft.Engine;

namespace LolDraft.Balance;

/// <summary>
/// Havuz tutarlılık kontrolü (DESIGN.md §16.13 adım 12): etiketlemedeki çelişkileri uyarır, havuzun özetini yazar.
/// Uyarı hata değildir (Kayle gibi bilinçli istisnalar olur); etiketleyene "bir daha bak" der.
/// Çalıştırma: dotnet run --project tools/LolDraft.Balance -- --check [--champions havuz.json]
/// </summary>
internal static class PoolCheck
{
    // Güç toplamı (e + m + l) bu aralığın dışındaysa uyar: örnek havuzda 15-21.
    private const int MinPowerSum = 14;
    private const int MaxPowerSum = 22;

    // 40 şampiyonluk havuzda rol başına önerilen en az şampiyon (fearless bo5'te rol başı 5 maç × 2 takım).
    private const int RecommendedPerRole = 6;

    public static void Print(ChampionPool pool, TextWriter w)
    {
        var champions = pool.All;
        var warnings = champions.SelectMany(c => Warnings(c, pool.RolesOf(c))).ToList();

        w.WriteLine($"Havuz kontrolü — {champions.Count} şampiyon");
        w.WriteLine();
        w.WriteLine("## Roller");
        foreach (var role in Enum.GetValues<Role>())
        {
            var count = pool.For(role).Count;
            w.WriteLine($"  {role,-8} {count,3}{(count < RecommendedPerRole ? $"   (öneri: en az {RecommendedPerRole})" : "")}");
        }

        w.WriteLine();
        w.WriteLine("## Sınıflar (ana / ikincil)");
        foreach (var championClass in Enum.GetValues<ChampionClass>())
        {
            var primary = champions.Count(c => c.PrimaryClass == championClass);
            var secondary = champions.Count(c => c.SecondaryClass == championClass);
            w.WriteLine($"  {championClass,-10} {primary,3} / {secondary,-3}");
        }

        w.WriteLine();
        w.WriteLine("## Etiket ortalamaları (0-3) ve 2+ olan şampiyon sayısı");
        foreach (var (name, tag) in TagList)
            w.WriteLine($"  {name,-5} {champions.Average(tag),4:0.00}   {champions.Count(c => tag(c) >= 2),3}");
        w.WriteLine($"  hiper taşıyıcı: {string.Join(", ", champions.Where(c => c.IsHyperCarry).Select(c => c.Name))}");

        w.WriteLine();
        w.WriteLine($"## Uyarılar ({warnings.Count})");
        foreach (var warning in warnings)
            w.WriteLine($"  - {warning}");
        if (warnings.Count == 0)
            w.WriteLine("  yok");
    }

    private static readonly (string Name, Func<ChampionProfile, int> Tag)[] TagList =
    [
        ("fl", c => c.Tags.Frontline), ("en", c => c.Tags.Engage), ("dis", c => c.Tags.Disengage),
        ("peel", c => c.Tags.Peel), ("poke", c => c.Tags.Poke), ("pick", c => c.Tags.Pick),
        ("dive", c => c.Tags.Dive), ("split", c => c.Tags.Split), ("mob", c => c.Tags.Mobility),
    ];

    internal static IEnumerable<string> Warnings(ChampionProfile c, IReadOnlyCollection<Role> roles)
    {
        var name = c.Name;
        var carryClass = c.HasClass(ChampionClass.Marksman) || c.HasClass(ChampionClass.Mage);

        // Hiper taşıyıcı tipi (§16.2): nişancı/büyücü + econ 3 + geç ≥ 9. Yarım tutan tanım çoğu zaman etiket hatası.
        if (carryClass && c.Late >= 9 && c.Economy < 3)
            yield return $"{name}: geç gücü {c.Late} ve {c.PrimaryClass}, ama econ {c.Economy}: hiper taşıyıcıysa econ 3 (K8, K9 buna bakar)";
        if (c.Economy == 3 && c.Late < 8)
            yield return $"{name}: econ 3 (item'sız işe yaramaz) ama geç gücü {c.Late}: altına bu kadar bağımlıysa geçte güçlü olmalı";

        if (c.PrimaryClass == ChampionClass.Tank && c.Tags.Frontline < 2)
            yield return $"{name}: tank ama fl {c.Tags.Frontline} (K10 frontline buna bakar)";
        if (c.PrimaryClass == ChampionClass.Tank && c.Economy >= 2)
            yield return $"{name}: tank ama econ {c.Economy}: tanklar genelde altınsız da işler (0-1)";
        if (c.PrimaryClass == ChampionClass.Enchanter && c.Tags.Peel + c.Tags.Disengage < 2)
            yield return $"{name}: enchanter ama peel + dis {c.Tags.Peel + c.Tags.Disengage}";
        if (c.HasClass(ChampionClass.Assassin) && c.Tags.Mobility < 2)
            yield return $"{name}: suikastçı ama mob {c.Tags.Mobility}: K3 (erken all-in) mob ≥ 2 ister, hiç çalışmaz";

        if (c.PrimaryClass == ChampionClass.Marksman && c.Range == AttackRange.Melee)
            yield return $"{name}: melee nişancı: lane'de melee formda mı? (range = koridor menzili, §16.2; Kayle gibi bilinçliyse sorun yok)";
        if (c.Tags.Split >= 2 && !roles.Contains(Role.Top) && !roles.Contains(Role.Mid))
            yield return $"{name}: split {c.Tags.Split} ama top/mid oynamıyor: K12 sadece top/mid'de çalışır";
        if (c.Tags.Engage >= 2 && roles.Count == 1 && roles.Contains(Role.Bot))
            yield return $"{name}: adc ama en {c.Tags.Engage}: engage genelde destek/ormancı/top işi";

        var powerSum = c.Early + c.Mid + c.Late;
        if (powerSum is < MinPowerSum or > MaxPowerSum)
            yield return $"{name}: güç toplamı (e + m + l) {powerSum}, beklenen {MinPowerSum}-{MaxPowerSum}: çok {(powerSum > MaxPowerSum ? "güçlü" : "zayıf")}";

        if (TagList.All(t => t.Tag(c) == 0))
            yield return $"{name}: hiç etiketi yok: kurallar onu görmez";
    }
}
