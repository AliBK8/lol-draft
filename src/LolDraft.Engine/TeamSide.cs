namespace LolDraft.Engine;

public enum TeamSide
{
    Blue,
    Red,
}

internal static class TeamSideExtensions
{
    /// <summary>Puan farkı Mavi açısından tutulur: Mavi +1, Kırmızı −1 (DESIGN.md §4.2).</summary>
    public static int Sign(this TeamSide side) => side == TeamSide.Blue ? 1 : -1;

    public static TeamSide Opponent(this TeamSide side) => side == TeamSide.Blue ? TeamSide.Red : TeamSide.Blue;

    public static string DisplayName(this TeamSide side) => side == TeamSide.Blue ? "Mavi" : "Kırmızı";
}
