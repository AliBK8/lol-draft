using System.Net;

namespace LolDraft.Gateway.Tests;

// İstemci anahtarı (DESIGN.md §8.5): IPv4 adresin kendisi, IPv6 /64 öneki.
public sealed class ClientKeyTests
{
    [Theory]
    [InlineData("203.0.113.50", "203.0.113.50")]
    [InlineData("::ffff:203.0.113.50", "203.0.113.50")]                       // IPv4'ün IPv6 kılığı
    [InlineData("2001:db8:1:2::a", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]     // aynı /64, aynı kova
    [InlineData("2001:db8:1:3::a", "2001:db8:1:3::/64")]                      // komşu /64 ayrı kova
    [InlineData("::1", "::/64")]
    public void Adres_anahtara_cevrilir(string address, string expected) =>
        Assert.Equal(expected, ClientKey.Of(IPAddress.Parse(address)));

    [Fact]
    public void Adres_yoksa_bilinmeyen() => Assert.Equal(ClientKey.Unknown, ClientKey.Of((IPAddress?)null));
}
