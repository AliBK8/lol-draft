using System.Net;
using System.Net.Sockets;

namespace LolDraft.Gateway;

/// <summary>
/// İstemcinin anahtarı: IP'ye bağlı her sınır (gateway rate limit'leri, API'deki "IP başına 3 lobi") bunu sayar
/// (DESIGN.md §8.5). IPv4'te adresin kendisi; IPv6'da ilk 64 bit (/64 öneki).
/// Neden /64: operatörler IPv6'da müşteriye tek adres değil bütün bir /64 bloğu verir (2⁶⁴ adres). Tam adrese göre
/// saysaydık tek kişi her istekte adres değiştirip bütün sınırları atlardı.
/// Örnek: 2001:db8:1:2::a ve 2001:db8:1:2:ffff::1 → ikisi de "2001:db8:1:2::/64" (aynı kova).
/// Adres önceden ForwardedHeaders'tan geçmiştir (sadece güvenilen proxy'nin X-Forwarded-For'u).
/// </summary>
internal static class ClientKey
{
    public const string Unknown = "unknown";

    public static string Of(HttpContext context) => Of(context.Connection.RemoteIpAddress);

    public static string Of(IPAddress? address)
    {
        if (address is null)
            return Unknown;

        // IPv4'ün IPv6 kılığı (::ffff:203.0.113.5; çift yığınlı soket böyle verir) aynı IPv4 istemcisidir.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        // İlk 8 bayt (64 bit) kalır, arayüz kısmı sıfırlanır.
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }
}
