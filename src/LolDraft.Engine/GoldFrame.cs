namespace LolDraft.Engine;

/// <summary>
/// Dakika karesi (DESIGN.md §16.11): <paramref name="Minute"/>. dakikanın başındaki (t = dk × 60) altınlar.
/// İlk kare 0. dakika (herkes 500); son kare maçın bittiği dakika, Nexus anındaki altınlar.
/// </summary>
/// <param name="Champions">Sıra: Mavi top, jungle, mid, bot, support, sonra Kırmızı aynı sırayla.</param>
public sealed record GoldFrame(int Minute, IReadOnlyList<ChampionGold> Champions)
{
    public int TeamGold(TeamSide side) => Champions.Where(c => c.Side == side).Sum(c => c.Gold);

    // Record eşitliği listeyi referansla karşılaştırır; aynı içerikli iki kare eşit sayılsın diye içerikle karşılaştır.
    public bool Equals(GoldFrame? other) =>
        other is not null && Minute == other.Minute && Champions.SequenceEqual(other.Champions);

    public override int GetHashCode() => HashCode.Combine(Minute, Champions.Count);
}

/// <param name="Gold">Aşağı yuvarlanmış altın.</param>
/// <param name="Cs">Kesilen minyon/kamp sayısı (aşağı yuvarlanmış); support'ta 0.</param>
public sealed record ChampionGold(string ChampionId, TeamSide Side, int Gold, int Cs);
