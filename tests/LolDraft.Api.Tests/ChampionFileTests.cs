using System.Text;
using LolDraft.Domain;
using LolDraft.Engine;
using LolDraft.Infrastructure.Champions;

namespace LolDraft.Api.Tests;

// Şampiyon dosyasının tek okuyucusu (oyun kataloğu + denge aracı). Gerçek dosya, API her açıldığında okunur
// (diğer testlerin hepsi uygulamayı açtığı için bozuk havuz onları da düşürür); burada okuyucunun kuralları.
public sealed class ChampionFileTests
{
    private const string Leona =
        """{ "id": "leona", "name": "Leona", "roles": ["sup"], "range": "melee", "classes": ["tank"], "e": 7, "m": 6, "l": 5, "econ": 0, "ap": 0.5, "tags": { "fl": 3, "en": 3 } }""";

    [Fact]
    public void Gecerli_kayit_profil_ve_rollerle_okunur()
    {
        var entry = Assert.Single(Parse(Leona.Replace("[\"sup\"]", "[\"sup\", \"top\"]")));

        Assert.Equal("leona", entry.Id.Value);
        Assert.Equal(ChampionClass.Tank, entry.Profile.PrimaryClass);
        Assert.Equal(3, entry.Profile.Tags.Engage);
        Assert.Equal(0, entry.Profile.Tags.Poke);
        Assert.Equal(new[] { Role.Support, Role.Top }, entry.Roles);
        // Motor da doğal rolleri bilir (rol dışı cezası); yuva sırasıyla.
        Assert.Equal(new[] { ChampionRole.Top, ChampionRole.Support }, entry.Profile.Roles);
    }

    [Theory]
    [InlineData("\"tank\"]", "\"healer\"]")]          // bilinmeyen sınıf
    [InlineData("\"sup\"]", "\"bot\"]")]              // bilinmeyen rol (adc olmalı)
    [InlineData("\"e\": 7", "\"e\": 11")]             // güç 1-10
    [InlineData("\"fl\": 3", "\"fl\": 4")]            // etiket 0-3
    [InlineData("\"id\": \"leona\"", "\"id\": \"Leona\"")]   // kimlik küçük harf
    [InlineData("\"roles\": [\"sup\"]", "\"roles\": []")]    // rolsüz
    public void Bozuk_kayit_reddedilir(string from, string to) =>
        Assert.Throws<InvalidDataException>(() => Parse(Leona.Replace(from, to)));

    [Fact]
    public void Ayni_sampiyon_iki_kez_yazilamaz() =>
        Assert.Throws<InvalidDataException>(() => Parse(Leona, Leona));

    private static IReadOnlyList<ChampionEntry> Parse(params string[] champions)
    {
        var json = $$"""{ "champions": [{{string.Join(",", champions)}}] }""";
        return ChampionFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }
}
