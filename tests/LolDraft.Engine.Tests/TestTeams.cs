using static LolDraft.Engine.AttackRange;
using static LolDraft.Engine.ChampionClass;

namespace LolDraft.Engine.Tests;

/// <summary>Prototipteki iki takım (DESIGN.md §3): Mavi erken comp, Kırmızı scale comp.</summary>
internal static class TestTeams
{
    public static TeamDraft EarlyBlue() => new(
        new ChampionProfile("renekton", "Renekton", Melee, Fighter, null, 9, 6, 4, economy: 1, apShare: 0,
            new ChampionTags(frontline: 2, engage: 2, pick: 1, dive: 2, split: 1, mobility: 2)),
        new ChampionProfile("leesin", "Lee Sin", Melee, Fighter, Assassin, 9, 6, 3, economy: 1, apShare: 0,
            new ChampionTags(frontline: 1, engage: 2, disengage: 1, peel: 1, pick: 3, dive: 2, mobility: 3)),
        new ChampionProfile("leblanc", "LeBlanc", Ranged, Assassin, Mage, 8, 7, 4, economy: 2, apShare: 1,
            new ChampionTags(poke: 2, pick: 3, dive: 2, split: 1, mobility: 3)),
        new ChampionProfile("draven", "Draven", Ranged, Marksman, null, 9, 6, 5, economy: 2, apShare: 0,
            new ChampionTags(mobility: 1)),
        new ChampionProfile("leona", "Leona", Melee, Tank, null, 8, 6, 5, economy: 0, apShare: 1,
            new ChampionTags(frontline: 3, engage: 3, peel: 1, pick: 2, dive: 1, mobility: 1)));

    public static TeamDraft ScaleRed() => new(
        new ChampionProfile("kayle", "Kayle", Melee, Marksman, Mage, 2, 6, 10, economy: 3, apShare: 0.5,
            new ChampionTags(peel: 1, poke: 1, split: 2, mobility: 1)),
        new ChampionProfile("sejuani", "Sejuani", Melee, Tank, null, 5, 7, 7, economy: 0, apShare: 1,
            new ChampionTags(frontline: 3, engage: 3, peel: 1, pick: 2, dive: 1, mobility: 1)),
        new ChampionProfile("kassadin", "Kassadin", Melee, Assassin, Mage, 3, 6, 10, economy: 3, apShare: 1,
            new ChampionTags(pick: 1, dive: 3, split: 2, mobility: 3)),
        new ChampionProfile("jinx", "Jinx", Ranged, Marksman, null, 4, 7, 9, economy: 3, apShare: 0,
            new ChampionTags(poke: 2, mobility: 1)),
        new ChampionProfile("lulu", "Lulu", Ranged, Enchanter, null, 6, 6, 7, economy: 0, apShare: 1,
            new ChampionTags(disengage: 2, peel: 3, poke: 1)));

    public static MatchInput Match(uint seed) => new(EarlyBlue(), ScaleRed(), new Seed(seed));

    /// <summary>Ezici draft: Mavi her fazda 10, Kırmızı her fazda 1. Koridor olaylarını bol üretir.</summary>
    public static MatchInput Stomp(uint seed) => new(Flat("b", 10), Flat("r", 1), new Seed(seed));

    /// <summary>Eşit draft: iki taraf her fazda 6, sinerji ve etiketler aynı. Fark sadece oyunda (altın, zar) açılır.</summary>
    public static MatchInput Even(uint seed) => new(Flat("b", 6), Flat("r", 6), new Seed(seed));

    /// <summary>
    /// Split senaryosu: eşit draft, ama Mavi'nin top'u güçlü bir splitter (split 3, rakibini 1v1 yener) ve Mavi'de
    /// frontline/engage yok (takım savaşında geride: K10, K5). Geride olan takımın split kozunu bol üretir.
    /// </summary>
    public static MatchInput Split(uint seed)
    {
        var blue = Flat("b", 6);
        var fiora = new ChampionProfile("fiora", "Fiora", Melee, Fighter, null, 8, 8, 8, economy: 2, apShare: 0,
            new ChampionTags(dive: 2, split: 3, mobility: 3));
        return new MatchInput(new TeamDraft(fiora, blue.Jungle, blue.Mid, blue.Bot, blue.Support), Flat("r", 6), new Seed(seed));
    }

    // İki takımda aynı yapı: sadece güç farklı. econ 2 (faz A'daki herkesin altın duyarlılığı), pick ve mob 0.
    private static TeamDraft Flat(string prefix, int power) => new(
        Flat($"{prefix}0", Melee, Fighter, power, 0, new ChampionTags(frontline: 2, engage: 2)),
        Flat($"{prefix}1", Melee, Fighter, power, 0, new ChampionTags()),
        Flat($"{prefix}2", Ranged, Mage, power, 1, new ChampionTags()),
        Flat($"{prefix}3", Ranged, Marksman, power, 0, new ChampionTags()),
        Flat($"{prefix}4", Ranged, Enchanter, power, 1, new ChampionTags()));

    private static ChampionProfile Flat(string id, AttackRange range, ChampionClass championClass, int power, double ap, ChampionTags tags) =>
        new(id, id, range, championClass, null, power, power, power, economy: 2, apShare: ap, tags);
}
