namespace LolDraft.Domain;

/// <summary>
/// Lobideki takım koltuğu. Seri boyunca sabittir; renk (<see cref="Side"/>) ise her maç değişir.
/// Skor ve kimlik buna bağlanır. 1v1'de koltukta bir oyuncu, 2v2'de iki oyuncu oturur.
/// </summary>
public enum TeamSlot
{
    A,
    B,
}

public static class TeamSlotExtensions
{
    public static TeamSlot Opponent(this TeamSlot team) => team == TeamSlot.A ? TeamSlot.B : TeamSlot.A;
}

/// <summary>Seri uzunluğu; sayı değeri en fazla oynanacak maç sayısıdır.</summary>
public enum SeriesFormat
{
    Bo1 = 1,
    Bo3 = 3,
    Bo5 = 5,
}

public enum SeriesEndReason
{
    /// <summary>Bir takım gereken galibiyet sayısına ulaştı.</summary>
    Played,

    /// <summary>Bir oyuncu seri sürerken uzun süre bağlı olmadı, rakibi hükmen kazandı.</summary>
    Forfeit,
}

/// <summary>Serinin bitmiş bir maçı: kim maviydi, kim kazandı, draft neydi, oynanan maçın kimliği (tekrar izleme).</summary>
public sealed record SeriesGame(int Number, TeamSlot Blue, TeamSlot Winner, DraftResult Draft, MatchId Match);
