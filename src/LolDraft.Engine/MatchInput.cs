namespace LolDraft.Engine;

public sealed record MatchInput
{
    public MatchInput(TeamDraft blue, TeamDraft red, Seed seed)
    {
        ArgumentNullException.ThrowIfNull(blue);
        ArgumentNullException.ThrowIfNull(red);

        var blueIds = blue.Champions.Select(c => c.Id).ToHashSet();
        if (red.Champions.Any(c => blueIds.Contains(c.Id)))
            throw new ArgumentException("Bir şampiyon iki takımda birden olamaz.", nameof(red));

        Blue = blue;
        Red = red;
        Seed = seed;
    }

    public TeamDraft Blue { get; }
    public TeamDraft Red { get; }
    public Seed Seed { get; }
}
