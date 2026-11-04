namespace LolDraft.Domain;

public enum DraftPhase
{
    Ban1,
    Pick1,
    Ban2,
    Pick2,
    Swap,
    Completed,
}

public enum DraftActionKind
{
    Ban,
    Pick,
}

/// <summary>Sıradaki 20 adımdan biri: hangi fazda, kim, ne yapacak.</summary>
public sealed record DraftStep(DraftPhase Phase, Side Side, DraftActionKind Kind);

/// <summary>
/// Turnuva sırası (DESIGN.md §2). Draft'ın durumu bu tablo + "kaçıncı adımdayız" sayacıdır;
/// faz ve sıra ayrı değişken olarak tutulmaz, tablodan okunur (birbirleriyle çelişemezler).
/// </summary>
public static class DraftOrder
{
    private const Side B = Side.Blue;
    private const Side R = Side.Red;

    public static IReadOnlyList<DraftStep> Steps { get; } =
    [
        // Ban 1: M1 K1 M2 K2 M3 K3
        ..Make(DraftPhase.Ban1, DraftActionKind.Ban, B, R, B, R, B, R),
        // Pick 1: M1 · K1 K2 · M2 M3 · K3
        ..Make(DraftPhase.Pick1, DraftActionKind.Pick, B, R, R, B, B, R),
        // Ban 2: K4 M4 K5 M5
        ..Make(DraftPhase.Ban2, DraftActionKind.Ban, R, B, R, B),
        // Pick 2: K4 · M4 M5 · K5
        ..Make(DraftPhase.Pick2, DraftActionKind.Pick, R, B, B, R),
    ];

    private static IEnumerable<DraftStep> Make(DraftPhase phase, DraftActionKind kind, params Side[] sides) =>
        sides.Select(side => new DraftStep(phase, side, kind));
}
