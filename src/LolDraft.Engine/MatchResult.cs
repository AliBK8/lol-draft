using LolDraft.Engine.Events;

namespace LolDraft.Engine;

/// <summary>Olaylar zaman sırasındadır; son olay her zaman <see cref="NexusEvent"/>.</summary>
/// <param name="GoldFrames">Dakika kareleri, 0. dakikadan maçın bittiği dakikaya (DESIGN.md §16.11).</param>
/// <param name="Report">Draft karnesi: etkili kurallar, taşıyıcılar, altın kaynakları (DESIGN.md §16.11).</param>
public sealed record MatchResult(
    IReadOnlyList<MatchEvent> Events, ScoreBreakdown Breakdown, MatchEnd End, IReadOnlyList<GoldFrame> GoldFrames,
    DraftReport Report);

public enum MatchEndKind
{
    /// <summary>Dk 35 son takım savaşı.</summary>
    FinalFight,

    /// <summary>Önde olanın kuşatmayı kazanması (DESIGN.md §4.6).</summary>
    Siege,
}

/// <param name="WinnerProbability">Maçı bitiren savaşta kazananın kazanma ihtimali (0.1-0.9).</param>
public sealed record MatchEnd(int Minute, TeamSide Winner, MatchEndKind Kind, double WinnerProbability);
