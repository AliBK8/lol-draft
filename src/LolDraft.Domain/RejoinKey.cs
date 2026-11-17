using System.Buffers.Text;

namespace LolDraft.Domain;

/// <summary>
/// Koltuk anahtarının özeti (DESIGN.md §8.4): oyuncu sekmeyi kapatıp linki yeniden açınca koltuğunu bununla geri alır.
/// Ham anahtar istemcide kalır; lobi sadece özetini (SHA-256, 32 bayt) tutar, özeti Application üretir.
/// Domain kriptografi bilmez: bu sadece karşılaştırılan bir değerdir.
/// </summary>
public readonly record struct RejoinKey
{
    public const int ByteLength = 32;

    private RejoinKey(string value) => Value = value;

    public string Value { get; }

    public static RejoinKey FromBytes(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != ByteLength)
            throw new ArgumentException($"Koltuk anahtarı özeti tam {ByteLength} bayttır.", nameof(hash));

        return new RejoinKey(Base64Url.EncodeToString(hash));
    }

    // Özet loglara ya da hata mesajlarına düşmesin.
    public override string ToString() => "RejoinKey(***)";
}
