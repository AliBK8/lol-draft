namespace LolDraft.Engine;

/// <summary>
/// Takımdaki beş yuva, <see cref="TeamDraft"/> sırasıyla. Hem şampiyonun oynadığı yuva hem de doğal rolleri
/// (<see cref="ChampionProfile.Roles"/>) bununla ifade edilir; ikisi uyuşmazsa rol dışı (DESIGN.md §16.8).
/// </summary>
public enum ChampionRole
{
    Top,
    Jungle,
    Mid,
    Bot,
    Support,
}
