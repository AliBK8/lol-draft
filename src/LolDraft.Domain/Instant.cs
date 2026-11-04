namespace LolDraft.Domain;

/// <summary>
/// Zamanda bir an. Domain saati kendisi okumaz; her işlem "şu an"ı parametre olarak alır,
/// böylece testler beklemeden "31 sn geçti" diyebilir (gerçek saat Application'daki IClock'ta).
/// </summary>
public readonly record struct Instant(DateTimeOffset Value) : IComparable<Instant>
{
    public int CompareTo(Instant other) => Value.CompareTo(other.Value);

    public static Instant operator +(Instant instant, TimeSpan span) => new(instant.Value + span);
    public static bool operator <(Instant left, Instant right) => left.Value < right.Value;
    public static bool operator >(Instant left, Instant right) => left.Value > right.Value;
    public static bool operator <=(Instant left, Instant right) => left.Value <= right.Value;
    public static bool operator >=(Instant left, Instant right) => left.Value >= right.Value;
}
