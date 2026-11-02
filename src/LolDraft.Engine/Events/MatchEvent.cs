namespace LolDraft.Engine.Events;

/// <summary>Maçta olan bir şey (DESIGN.md §4.9). <paramref name="T"/> oyun saniyesi; boş <paramref name="Text"/> log'a yazılmaz.</summary>
public abstract record MatchEvent(double T, string Text);

public enum Objective
{
    Dragon,
    Herald,
    Baron,
    Elder,
    FinalFight,
}

/// <param name="Probability">Kazananın bu savaşı kazanma ihtimali (0.1-0.9).</param>
public sealed record ObjectiveEvent(double T, string Text, Objective Objective, TeamSide Side, double Probability)
    : MatchEvent(T, Text);

/// <param name="Side">Kill'i alan takım.</param>
/// <param name="Until">Kurbanın yeniden doğduğu oyun saniyesi.</param>
/// <param name="Lane">Koridor olayıysa koridor; savaş kill'lerinde boş.</param>
/// <param name="Bounty">Katilin kill altınına ek aldığı shutdown ödülü (0-700, DESIGN.md §16.4).</param>
public sealed record KillEvent(
    double T, string Text, TeamSide Side, string KillerId, string VictimId, double Until, Lane? Lane, int Bounty)
    : MatchEvent(T, Text);

/// <param name="Tier">0 dış kule, 1 iç kule, 2 inhibitör.</param>
public sealed record TowerEvent(double T, string Text, TeamSide Side, Lane Lane, int Tier) : MatchEvent(T, Text);

public sealed record PlateEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

public sealed record CsEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

/// <param name="Value">Sürprizin kazandırdığı puan (3-5).</param>
public sealed record SurpriseEvent(double T, string Text, TeamSide Side, Lane Lane, double Value) : MatchEvent(T, Text);

/// <param name="Side">Gank'ı atan takım.</param>
public sealed record GankFailEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

/// <summary>
/// Görünmez hareket ipucu: <paramref name="ParticipantIds"/> olaydan <paramref name="Lead"/> sn önce hedefe yürür,
/// <paramref name="Tail"/> sn kalır.
/// </summary>
/// <param name="Lane">Gank hedefi koridor; nehir çatışmasında boş.</param>
/// <param name="River">Nehir çatışmasının yeri; gank'ta boş.</param>
public sealed record MoveEvent(
    double T, string Text, IReadOnlyList<string> ParticipantIds, Lane? Lane, RiverSide? River, int Lead, int Tail)
    : MatchEvent(T, Text)
{
    // Record eşitliği listeyi referansla karşılaştırır; aynı içerikli iki olay eşit sayılsın diye içerikle karşılaştır.
    public bool Equals(MoveEvent? other) =>
        other is not null
        && base.Equals(other)
        && ParticipantIds.SequenceEqual(other.ParticipantIds)
        && Lane == other.Lane && River == other.River && Lead == other.Lead && Tail == other.Tail;

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), ParticipantIds.Count, Lane, River, Lead, Tail);
}

/// <param name="Side">Çatışmayı kazanan takım.</param>
/// <param name="Probability">Kazananın bu çatışmayı kazanma ihtimali (0.1-0.9).</param>
public sealed record SkirmishEvent(
    double T, string Text, TeamSide Side, double Probability, RiverSide River, int BlueCount, int RedCount)
    : MatchEvent(T, Text);

/// <summary>Görünmez hareket ipucu: takım <paramref name="Lead"/> sn önce toplanır, <paramref name="Tail"/> sn kalır.</summary>
public sealed record SiegeEvent(double T, string Text, TeamSide Side, int Lead, int Tail) : MatchEvent(T, Text);

/// <summary>
/// Split (DESIGN.md §16.8 K12): objektif savaşı sırasında <paramref name="SplitterId"/> yan koridorda baskı kurar.
/// Cevap veren rakip (<paramref name="AnswererId"/>) varsa ikisi düello yapar; yoksa splitter kuleyi karşılıksız alır.
/// </summary>
public sealed record SplitEvent(double T, string Text, TeamSide Side, Lane Lane, string SplitterId, string? AnswererId)
    : MatchEvent(T, Text);

/// <summary>4. ejderi alan takım ejder ruhunu kazanır; ardından Elder doğar.</summary>
public sealed record DragonSoulEvent(double T, string Text, TeamSide Side) : MatchEvent(T, Text);

/// <param name="Side">Kuşatmayı tutan (savunan) takım.</param>
/// <param name="Probability">Savunanın kuşatmayı tutma ihtimali.</param>
public sealed record ThrowEvent(double T, string Text, TeamSide Side, double Probability) : MatchEvent(T, Text);

/// <summary>Maçın son olayı.</summary>
/// <param name="Probability">Kuşatmayla bittiyse kuşatmanın başarı ihtimali; son savaşla bittiyse boş.</param>
public sealed record NexusEvent(double T, string Text, TeamSide Side, double? Probability = null) : MatchEvent(T, Text);
