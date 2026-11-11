using System.Buffers.Text;
using System.Text.RegularExpressions;

namespace LolDraft.Domain;

/// <summary>
/// Kayıtsız oyuncunun kimliği (JWT'deki sub): 128 bit rastgele, URL-güvenli base64 (22 karakter).
/// Lobiye girerken sunucu üretir; oyuncu seçemez.
/// </summary>
public readonly partial record struct PlayerId
{
    public const int ByteLength = 16;

    private PlayerId(string value) => Value = value;

    public string Value { get; }

    public static Result<PlayerId> Create(string? value) =>
        value is not null && Format().IsMatch(value)
            ? Result<PlayerId>.Ok(new PlayerId(value))
            : Result<PlayerId>.Fail(LobbyErrors.InvalidPlayerId);

    public static PlayerId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException($"Oyuncu kimliği tam {ByteLength} bayttan üretilir.", nameof(bytes));

        return new PlayerId(Base64Url.EncodeToString(bytes));
    }

    public override string ToString() => Value;

    // \z: dizinin tam sonu ($ sondaki \n'yi kabul ederdi; ChampionId'deki not).
    [GeneratedRegex(@"^[A-Za-z0-9_-]{22}\z")]
    private static partial Regex Format();
}
