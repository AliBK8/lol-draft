using System.Net;

namespace LolDraft.Gateway.Tests;

/// <summary>
/// İstemci build'inin sunulması (DESIGN.md §8.17): statik dosyalar, SPA yönlendirmesi, önbellek başlıkları ve
/// API beyaz listesinin bundan etkilenmemesi. Geçici bir wwwroot'la.
/// </summary>
public sealed class WebClientTests : IDisposable
{
    private const string IndexHtml = "<!doctype html><title>LoL Draft</title><div id=\"root\"></div>";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "lol-draft-web", Guid.NewGuid().ToString("N"));
    private readonly GatewayPipeline _pipeline;
    private readonly HttpClient _client;

    public WebClientTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "champions"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-abc123.js"), "console.log(1)");
        File.WriteAllBytes(Path.Combine(_webRoot, "champions", "jinx.png"), [0x89, 0x50, 0x4E, 0x47]);

        _pipeline = new GatewayPipeline(webRoot: _webRoot);
        _client = _pipeline.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/l/AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("/m/AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Istemci_yollari_index_html_doner_ve_onbellege_alinmaz(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        // Güvenlik başlıkları statik cevaplara da girer.
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Empty(_pipeline.Probe.Requests);
    }

    [Fact]
    public async Task Hashli_dosya_kalici_onbellekte_kalir()
    {
        var response = await _client.GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log(1)", await response.Content.ReadAsStringAsync());
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Sampiyon_resmi_bir_gun_onbellekte_kalir()
    {
        var response = await _client.GetAsync("/champions/jinx.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
    }

    // Olmayan dosya index.html değil 404: tarayıcı HTML'i script diye çalıştırmaya kalkmaz, hata görünür kalır.
    [Theory]
    [InlineData("/assets/yok-999.js")]
    [InlineData("/champions/yok.png")]
    public async Task Olmayan_dosya_404(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Beyaz liste korunur: listede olmayan API ya da hub yolu index.html ile "başarılı" görünmez, API'ye de ulaşmaz.
    [Theory]
    [InlineData("/api/admin")]
    [InlineData("/api")]
    [InlineData("/API/admin")]
    [InlineData("/hubs/baska")]
    public async Task Api_ve_hub_yollari_spaya_dusmez(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<!doctype", await response.Content.ReadAsStringAsync());
        Assert.Empty(_pipeline.Probe.Requests);
    }

    [Fact]
    public async Task Sampiyon_listesi_tokensiz_apiye_ulasir()
    {
        var response = await _client.GetAsync("/api/champions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_pipeline.Probe.Reached("/api/champions"));
        Assert.Contains("\"champions\"", await response.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (IOException)
        {
            // Geçici klasör; silinemezse işletim sistemi temizler.
        }
    }
}
