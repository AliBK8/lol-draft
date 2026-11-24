using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

public sealed class TeamNameTests
{
    [Theory]
    [InlineData("Kuzey Kurtları", "Kuzey Kurtları")]
    [InlineData("  Fener   Akademi ", "Fener Akademi")] // kırpılır, aradaki boşluk teke iner
    [InlineData("T1", "T1")]
    [InlineData("O'Neil_.-", "O'Neil_.-")]
    [InlineData("16_karakter_tamm", "16_karakter_tamm")]
    public void Kurala_uyan_ad_temizlenip_kabul_edilir(string input, string expected) =>
        Assert.Equal(expected, TeamName.Create(input).Value.Value);

    [Fact]
    public void Ayni_harfin_iki_yazilisi_ayni_ada_cikar()
    {
        // "ş" tek karakter (U+015F) ya da "s" + birleşik çengel (U+0327): NFC ikisini birleştirir.
        var composed = TeamName.Create("Kaş").Value;
        var decomposed = TeamName.Create("Kaş").Value;

        Assert.Equal(composed, decomposed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]                        // 2'den kısa
    [InlineData("17_karakter_tamam")]        // 16'dan uzun
    [InlineData("<b>")]                      // HTML karakteri
    [InlineData("Ters‮yazı")]          // sağdan sola işareti (görünmez)
    [InlineData("Sıfır​genişlik")]     // sıfır genişlikli boşluk
    [InlineData("Satır\nsonu")]
    [InlineData("😀 Takım")]                 // emoji (harf değil)
    public void Kurala_uymayan_ad_reddedilir(string? input) =>
        Assert.Equal(LobbyErrors.InvalidTeamName, TeamName.Create(input).Error);

    [Fact]
    public void Lobi_koltuga_verilen_adi_tutar_vermeyen_adsiz()
    {
        var name = TeamName.Create("Kurtlar").Value;
        var lobby = Lobby.Create(LobbyCode.FromBytes(new byte[16]), Player(1), SeriesFormat.Bo3, false, Pool,
            TeamSlot.A, T0, name).Value;
        lobby.Join(Player(2), T0);

        Assert.Equal(name, lobby.NameOf(TeamSlot.A));
        Assert.Null(lobby.NameOf(TeamSlot.B));
    }

    private static PlayerId Player(byte n) => PlayerId.FromBytes(Enumerable.Repeat(n, PlayerId.ByteLength).ToArray());
}
