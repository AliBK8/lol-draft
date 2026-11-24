namespace LolDraft.Engine;

public sealed record MatchInput
{
    /// <param name="bluePlan">Mavi'nin oyun planı (DESIGN.md §4.10); seçmeyen Dengeli oynar.</param>
    /// <param name="redPlan">Kırmızı'nın oyun planı.</param>
    public MatchInput(
        TeamDraft blue, TeamDraft red, Seed seed, GamePlan bluePlan = GamePlan.Balanced, GamePlan redPlan = GamePlan.Balanced)
    {
        ArgumentNullException.ThrowIfNull(blue);
        ArgumentNullException.ThrowIfNull(red);

        var blueIds = blue.Champions.Select(c => c.Id).ToHashSet();
        if (red.Champions.Any(c => blueIds.Contains(c.Id)))
            throw new ArgumentException("Bir şampiyon iki takımda birden olamaz.", nameof(red));

        if (!Enum.IsDefined(bluePlan))
            throw new ArgumentOutOfRangeException(nameof(bluePlan), bluePlan, "Bilinmeyen plan.");
        if (!Enum.IsDefined(redPlan))
            throw new ArgumentOutOfRangeException(nameof(redPlan), redPlan, "Bilinmeyen plan.");

        Blue = blue;
        Red = red;
        Seed = seed;
        BluePlan = bluePlan;
        RedPlan = redPlan;
    }

    public TeamDraft Blue { get; }
    public TeamDraft Red { get; }
    public Seed Seed { get; }
    public GamePlan BluePlan { get; }
    public GamePlan RedPlan { get; }
}
