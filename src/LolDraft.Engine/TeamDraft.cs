using System.Collections.ObjectModel;

namespace LolDraft.Engine;

/// <summary>Bir takımın 5 rolü. Eksik rol ya da aynı şampiyonun iki kez seçilmesi mümkün değildir.</summary>
public sealed record TeamDraft
{
    private readonly ReadOnlyCollection<ChampionProfile> _champions;

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
        // Liste bir kez kurulur: motor bunu dakikada yüzlerce kez okur (güç, altın, rakip eşlemesi).
        _champions = Array.AsReadOnly([top, jungle, mid, bot, support]);

        if (Champions.Select(c => c.Id).Distinct().Count() != Champions.Count)
            throw new ArgumentException("Aynı şampiyon takımda iki kez olamaz.");
    }

    public ChampionProfile Top { get; }
    public ChampionProfile Jungle { get; }
    public ChampionProfile Mid { get; }
    public ChampionProfile Bot { get; }
    public ChampionProfile Support { get; }

    /// <summary>Sıra sabit: top, jungle, mid, bot, support.</summary>
    public IReadOnlyList<ChampionProfile> Champions => _champions;

    // Eşitlik beş rolün değeriyle. Elle yazıldı: derleyicinin ürettiği eşitlik _champions alanını referansla
    // karşılaştırır ve aynı içerikli iki takımı farklı sayardı.
    public bool Equals(TeamDraft? other) =>
        other is not null && Top == other.Top && Jungle == other.Jungle && Mid == other.Mid && Bot == other.Bot
        && Support == other.Support;

    public override int GetHashCode() => HashCode.Combine(Top, Jungle, Mid, Bot, Support);
}
