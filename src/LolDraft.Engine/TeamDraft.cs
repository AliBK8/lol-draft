namespace LolDraft.Engine;

/// <summary>Bir takımın 5 rolü. Eksik rol ya da aynı şampiyonun iki kez seçilmesi mümkün değildir.</summary>
public sealed record TeamDraft
{
    public TeamDraft(ChampionProfile top, ChampionProfile jungle, ChampionProfile mid, ChampionProfile bot, ChampionProfile support)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(jungle);
        ArgumentNullException.ThrowIfNull(mid);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(support);

        Top = top;
        Jungle = jungle;
        Mid = mid;
        Bot = bot;
        Support = support;

        if (Champions.Select(c => c.Id).Distinct().Count() != Champions.Count)
            throw new ArgumentException("Aynı şampiyon takımda iki kez olamaz.");
    }

    public ChampionProfile Top { get; }
    public ChampionProfile Jungle { get; }
    public ChampionProfile Mid { get; }
    public ChampionProfile Bot { get; }
    public ChampionProfile Support { get; }

    /// <summary>Sıra sabit: top, jungle, mid, bot, support. Alan değil, böylece record eşitliğine girmez.</summary>
    public IReadOnlyList<ChampionProfile> Champions => [Top, Jungle, Mid, Bot, Support];
}
