namespace LolDraft.Domain;

/// <summary>Haritadaki taraf. Engine'in TeamSide'ının Domain karşılığı; çeviri Application'da (Domain Engine'i bilmez).</summary>
public enum Side
{
    Blue,
    Red,
}

public static class SideExtensions
{
    public static Side Opponent(this Side side) => side == Side.Blue ? Side.Red : Side.Blue;
}
