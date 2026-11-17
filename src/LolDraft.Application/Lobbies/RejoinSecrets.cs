using System.Buffers.Text;
using System.Security.Cryptography;
using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Koltuk anahtarı (DESIGN.md §8.4): oyuncuya verilen ham anahtar ve lobinin tuttuğu özeti.
/// Ham anahtar 32 bayt CSPRNG (tahmin edilemez, 2^256), URL-güvenli base64 (43 karakter); sadece istemcide durur.
/// Lobi SHA-256 özetini tutar: bellek dökülse bile anahtar ele geçmez. Rastgele bir anahtarda tuz gerekmez
/// (sözlük saldırısı yok), yavaş özet de gerekmez (kaba kuvvet zaten imkânsız).
/// </summary>
internal static class RejoinSecrets
{
    private const int TokenBytes = 32;
    private const int TokenLength = 43;   // 32 bayt → 43 karakter base64url (dolgusuz)

    /// <summary>Yeni ham anahtar ve özeti.</summary>
    public static (string Token, RejoinKey Key) New(ISecureRandom random)
    {
        var bytes = random.Bytes(TokenBytes);
        return (Base64Url.EncodeToString(bytes), Hash(bytes));
    }

    /// <summary>İstemcinin gönderdiği anahtarın özeti; biçimi bozuksa null (özetlenmeden reddedilir).</summary>
    public static RejoinKey? KeyOf(string? token)
    {
        if (token is not { Length: TokenLength } || !Base64Url.IsValid(token))
            return null;

        return Hash(Base64Url.DecodeFromChars(token));
    }

    private static RejoinKey Hash(byte[] bytes) => RejoinKey.FromBytes(SHA256.HashData(bytes));
}
