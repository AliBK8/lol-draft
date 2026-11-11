using System.Text.RegularExpressions;

namespace LolDraft.Domain;

/// <summary>
/// Şampiyon kimliği ("leesin", "kaisa"). İstemciden gelir, o yüzden biçim katı:
/// sadece küçük harf ve rakam, 1-32 karakter. Geçersiz değerle nesne oluşmaz.
/// </summary>
public readonly partial record struct ChampionId
{
    private ChampionId(string value) => Value = value;

    public string Value { get; }

    public static Result<ChampionId> Create(string? value) =>
        value is not null && Format().IsMatch(value)
            ? Result<ChampionId>.Ok(new ChampionId(value))
            : Result<ChampionId>.Fail(DraftErrors.InvalidChampionId);

    public override string ToString() => Value;

    // \z: dizinin tam sonu. $ kullanılmaz: .NET'te $ sondaki tek bir \n'nin önünde de eşleşir ("zed\n" geçerdi).
    [GeneratedRegex(@"^[a-z0-9]{1,32}\z")]
    private static partial Regex Format();
}
