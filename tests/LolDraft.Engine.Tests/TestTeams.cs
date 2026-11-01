namespace LolDraft.Engine.Tests;

/// <summary>Prototipteki iki takım (DESIGN.md §3): Mavi erken comp, Kırmızı scale comp.</summary>
internal static class TestTeams
{
    public static TeamDraft EarlyBlue() => new(
        new ChampionProfile("renekton", "Renekton", 9, 6, 4, frontline: 2, engage: 2, apShare: 0),
        new ChampionProfile("leesin", "Lee Sin", 9, 6, 3, frontline: 0, engage: 2, apShare: 0),
        new ChampionProfile("leblanc", "LeBlanc", 8, 7, 4, frontline: 0, engage: 0, apShare: 1),
        new ChampionProfile("draven", "Draven", 9, 6, 5, frontline: 0, engage: 0, apShare: 0),
        new ChampionProfile("leona", "Leona", 8, 6, 5, frontline: 3, engage: 3, apShare: 1));

    public static TeamDraft ScaleRed() => new(
        new ChampionProfile("kayle", "Kayle", 2, 6, 10, frontline: 0, engage: 0, apShare: 0.5),
        new ChampionProfile("sejuani", "Sejuani", 5, 7, 7, frontline: 3, engage: 3, apShare: 1),
        new ChampionProfile("kassadin", "Kassadin", 3, 6, 10, frontline: 0, engage: 0, apShare: 1),
        new ChampionProfile("jinx", "Jinx", 4, 7, 9, frontline: 0, engage: 0, apShare: 0),
        new ChampionProfile("lulu", "Lulu", 6, 6, 7, frontline: 0, engage: 0, apShare: 1));

    public static MatchInput Match(uint seed) => new(EarlyBlue(), ScaleRed(), new Seed(seed));

    /// <summary>Ezici draft: Mavi her fazda 10, Kırmızı her fazda 1. Koridor olaylarını bol üretir.</summary>
    public static MatchInput Stomp(uint seed) => new(Flat("b", 10), Flat("r", 1), new Seed(seed));

    /// <summary>Eşit draft: iki taraf her fazda 6, sinerji aynı. Fark sadece oyunda (altın, zar) açılır.</summary>
    public static MatchInput Even(uint seed) => new(Flat("b", 6), Flat("r", 6), new Seed(seed));

    private static TeamDraft Flat(string prefix, int power) => new(
        new ChampionProfile($"{prefix}0", $"{prefix}0", power, power, power, frontline: 2, engage: 2, apShare: 0),
        new ChampionProfile($"{prefix}1", $"{prefix}1", power, power, power, frontline: 0, engage: 0, apShare: 0),
        new ChampionProfile($"{prefix}2", $"{prefix}2", power, power, power, frontline: 0, engage: 0, apShare: 1),
        new ChampionProfile($"{prefix}3", $"{prefix}3", power, power, power, frontline: 0, engage: 0, apShare: 0),
        new ChampionProfile($"{prefix}4", $"{prefix}4", power, power, power, frontline: 0, engage: 0, apShare: 1));
}
