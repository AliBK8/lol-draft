using System.Globalization;
using System.Text;

namespace LolDraft.Domain;

/// <summary>
/// Oyuncunun lobiye girerken verdiği takım adı (isteğe bağlı; seri bandında ve maç ekranında görünür).
/// İstemciden gelir, rakibin ekranına çizilir: biçim katı, geçersiz değerle nesne oluşmaz (DESIGN.md §8.6).
/// Kurallar: baş/son boşluk kırpılır, aradaki boşluk dizisi teke iner; 2-16 karakter; sadece harf, rakam,
/// boşluk ve <c>. - _ '</c>. Görünmez ve yön değiştiren karakterler (ör. U+202E sağdan sola) harf olmadığı için
/// kendiliğinden dışarıda kalır. Örnek: "  Fener   Akademi " → "Fener Akademi"; "a" → hata (çok kısa).
/// </summary>
public readonly record struct TeamName
{
    public const int MinLength = 2;
    public const int MaxLength = 16;

    private TeamName(string value) => Value = value;

    public string Value { get; }

    public static Result<TeamName> Create(string? value)
    {
        if (value is null)
            return Result<TeamName>.Fail(LobbyErrors.InvalidTeamName);

        // NFC: "ş" tek karakter mi, "s + çengel" mi; ikisi aynı ada çıksın, uzunluk da aynı sayılsın.
        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var c in normalized)
        {
            if (c == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (!Allowed(c))
                return Result<TeamName>.Fail(LobbyErrors.InvalidTeamName);

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }

        var name = builder.ToString();
        return name.Length is >= MinLength and <= MaxLength
            ? Result<TeamName>.Ok(new TeamName(name))
            : Result<TeamName>.Fail(LobbyErrors.InvalidTeamName);
    }

    public override string ToString() => Value;

    // Harf (tüm diller, birleşik işaretler dahil), rakam ve birkaç noktalama. Sekme, satır sonu, sıfır genişlikli
    // ve yön karakterleri Cc/Cf kategorisinde: hiçbiri burada yok.
    private static bool Allowed(char c) =>
        c is '.' or '-' or '_' or '\''
        || char.GetUnicodeCategory(c) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber;
}
