using System.Buffers.Text;
using System.Text.RegularExpressions;

namespace LolDraft.Domain;

/// <summary>
/// Oynanmış bir maçın kimliği (tekrar izleme linki, DESIGN.md §8.7): 128 bit rastgele, URL-güvenli base64 (22 karakter).
/// Link herkese açık; tahmin edilemez olması maçın gizliliğidir (lobi kodu gibi). Baytlar dışarıdan (CSPRNG) gelir.
/// </summary>
public readonly partial record struct MatchId
{
    public const int ByteLength = 16;

    private MatchId(string value) => Value = value;

    public string Value { get; }

    public static Result<MatchId> Create(string? value) =>
        value is not null && Format().IsMatch(value)
            ? Result<MatchId>.Ok(new MatchId(value))
            : Result<MatchId>.Fail(MatchErrors.InvalidMatchId);

    public static MatchId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException($"Maç kimliği tam {ByteLength} bayttan üretilir.", nameof(bytes));

        return new MatchId(Base64Url.EncodeToString(bytes));
    }

    public override string ToString() => Value;

    // \z: dizinin tam sonu ($ sondaki \n'yi kabul ederdi; ChampionId'deki not).
    [GeneratedRegex(@"^[A-Za-z0-9_-]{22}\z")]
    private static partial Regex Format();
}

/// <summary>Maçın beklenen hataları. Kodlar istemciyle sözleşmedir, değiştirilmez.</summary>
public static class MatchErrors
{
    public static readonly Error InvalidMatchId = new("invalid_match_id", "Geçersiz maç kimliği.");
    public static readonly Error NotFound = new("match_not_found", "Maç bulunamadı.");
}
