using System.Buffers.Text;
using System.Text.RegularExpressions;

namespace LolDraft.Domain;

/// <summary>
/// Lobinin davet kodu (DESIGN.md §8.4): 128 bit rastgele, URL-güvenli base64 (22 karakter).
/// Tahmin edilemez olması davetin tek güvenliğidir; rastgele baytlar dışarıdan (CSPRNG) gelir.
/// İstemciden de geldiği için biçim katı: geçersiz değerle nesne oluşmaz.
/// </summary>
public readonly partial record struct LobbyCode
{
    public const int ByteLength = 16;

    private LobbyCode(string value) => Value = value;

    public string Value { get; }

    public static Result<LobbyCode> Create(string? value) =>
        value is not null && Format().IsMatch(value)
            ? Result<LobbyCode>.Ok(new LobbyCode(value))
            : Result<LobbyCode>.Fail(LobbyErrors.InvalidLobbyCode);

    public static LobbyCode FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException($"Lobi kodu tam {ByteLength} bayttan üretilir.", nameof(bytes));

        // 16 bayt → 22 karakter; base64url'de '+', '/' ve '=' yok, linke olduğu gibi konur.
        return new LobbyCode(Base64Url.EncodeToString(bytes));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Za-z0-9_-]{22}$")]
    private static partial Regex Format();
}
