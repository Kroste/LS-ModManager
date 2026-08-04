using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

public sealed class ModHubServiceTests
{
    [Fact]
    public void BuildListUrl_EnthaeltFs2025SlugUndSprache()
    {
        var url = ModHubServiceTestHelper.BuildListUrl(1, "de");
        url.Should().StartWith("https://www.farming-simulator.com/mods.php");
        url.Should().Contain("title=fs2025");
        url.Should().Contain("lang=de");
        url.Should().NotContain("page=");
    }

    [Fact]
    public void BuildListUrl_HaengtPageAbSeite2An_0Basiert()
    {
        // GIANTS ist 0-basiert: unser 1-basiertes page=3 mappt auf URL &page=2.
        var url = ModHubServiceTestHelper.BuildListUrl(3, "en");
        url.Should().Contain("page=2");
        url.Should().Contain("lang=en");
    }

    [Fact]
    public void ParseListPage_ExtrahiertTitelAutorPreviewAusGiantsCard()
    {
        // GIANTS-Card-Struktur: div.machines--mods > machines__content > h3 + Von-Span
        var html = """
            <html><body>
              <div class="machines machines--mods">
                <div class="dlc__title clearfix"><h4>EMPFOHLENER MOD</h4></div>
                <div class="machines__content">
                  <div class="machines__img">
                    <a href="mod.php?mod_id=352048&title=fs2025"><img src="https://cdn.example/00352048/iconBig.jpg"></a>
                  </div>
                  <div class="machines__overview">
                    <h3>Solek</h3>
                    <p>Landw. Simulator 25<br>
                      <span>Von: RajotGPLAY</span></p>
                    <a href="mod.php?mod_id=352048&title=fs2025" class="button">MEHR INFO</a>
                  </div>
                </div>
              </div>
              <div class="machines machines--mods">
                <div class="dlc__title clearfix"><h4>BELIEBTESTER MOD</h4></div>
                <div class="machines__content">
                  <div class="machines__img">
                    <a href="mod.php?mod_id=305991&title=fs2025"><img src="/local/preview.jpg"></a>
                  </div>
                  <div class="machines__overview">
                    <h3>Ballen Autoload Pack</h3>
                    <p>Landw. Simulator 25<br><span>Von: [Weekend Farmers] Amarok-10</span></p>
                  </div>
                </div>
              </div>
            </body></html>
            """;

        var entries = ModHubService.ParseListPage(html);
        entries.Should().HaveCount(2);

        entries[0].Title.Should().Be("Solek");
        entries[0].Author.Should().Be("RajotGPLAY");
        entries[0].Category.Should().Be("EMPFOHLENER MOD");
        entries[0].PreviewUrl.Should().Be("https://cdn.example/00352048/iconBig.jpg");
        entries[0].DetailUrl.Should().Contain("mod_id=352048");

        entries[1].Title.Should().Be("Ballen Autoload Pack");
        entries[1].Author.Should().Be("[Weekend Farmers] Amarok-10");
        entries[1].PreviewUrl.Should().StartWith("https://www.farming-simulator.com/local/preview.jpg");
    }

    [Fact]
    public void ParseListPage_IgnoriertDoppelteModIds()
    {
        var html = """
            <html><body>
              <div class="machines machines--mods">
                <div class="machines__content"><div class="machines__overview"><h3>A</h3>
                  <a href="mod.php?mod_id=1&title=fs2025">Cover</a>
                  <a href="mod.php?mod_id=1&extra=x">Mehr</a>
                </div></div>
              </div>
              <div class="machines machines--mods">
                <div class="machines__content"><div class="machines__overview"><h3>B</h3>
                  <a href="mod.php?mod_id=2&title=fs2025">Cover</a>
                </div></div>
              </div>
            </body></html>
            """;
        var entries = ModHubService.ParseListPage(html);
        entries.Should().HaveCount(2);
        entries.Select(e => e.Title).Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public void ParseListPage_LeeresHtml_LiefertLeereListe()
    {
        var entries = ModHubService.ParseListPage("<html><body>Nichts hier</body></html>");
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseListPage_ExtrahiertModItemStruktur()
    {
        // Katalog-Liste (nicht Empfehlungen): mod-item mit h4 + optional mod-label.
        var html = """
            <html><body>
              <div class="mod-item">
                <div class="mod-item__img">
                  <div class="mod-label mod-label-new">NEW!</div>
                  <a href="mod.php?mod_id=369672&title=fs2025"><img src="https://cdn.example/369672.jpg"></a>
                </div>
                <div class="mod-item__content">
                  <h4> Volvo L90 Pack</h4>
                  <p><span>Von: GIANTS Software</span></p>
                </div>
                <a href="mod.php?mod_id=369672&title=fs2025" class="button">MEHR INFO</a>
              </div>
            </body></html>
            """;

        var entries = ModHubService.ParseListPage(html);
        entries.Should().ContainSingle();
        entries[0].Title.Should().Be("Volvo L90 Pack");
        entries[0].Author.Should().Be("GIANTS Software");
        entries[0].Category.Should().Be("NEW!");
        entries[0].PreviewUrl.Should().Be("https://cdn.example/369672.jpg");
    }

    [Fact]
    public void ParseDetailPage_ExtrahiertKomplettesMetadaten()
    {
        var html = """
            <html><body>
              <h2>Solek</h2>
              <div class="mod-description">Ein tolle Karte<br />mit Traktoren.<br />Changelog 1.1.0.0<br />- Alles besser.</div>
              <div class="table-game-info">
                <div class="table-row"><div class="table-cell"><b>Spiel</b></div><div class="table-cell">Landw. Simulator 25</div></div>
                <div class="table-row"><div class="table-cell"><b>Kategorie</b></div><div class="table-cell"><a>Europäische Karten</a></div></div>
                <div class="table-row"><div class="table-cell"><b>Autor</b></div><div class="table-cell"><a>RajotGPLAY</a></div></div>
                <div class="table-row"><div class="table-cell"><b>Dateiname</b></div><div class="table-cell">FS25_Solek.zip</div></div>
                <div class="table-row"><div class="table-cell"><b>Grösse</b></div><div class="table-cell">1225.69 MB</div></div>
                <div class="table-row"><div class="table-cell"><b>Version</b></div><div class="table-cell">1.1.0.0</div></div>
                <div class="table-row"><div class="table-cell"><b>Veröffentlichung</b></div><div class="table-cell">19.06.2026</div></div>
                <div class="table-row"><div class="table-cell"><b>Plattform</b></div><div class="table-cell">PC/MAC, PS5, XBS</div></div>
              </div>
              <div class="mod-item__rating-num">4.5&nbsp;(4157)</div>
              <img src="https://cdn40.giants-software.com/modHub/storage/00352048/screenshot0.jpg">
              <img src="https://cdn40.giants-software.com/modHub/storage/00352048/screenshot1.jpg">
              <img src="https://cdn32.giants-software.com/modHub/storage/00352048/iconBig.jpg">
              <a href="https://cdn27.giants-software.com/modHub/storage/00352048/FS25_Solek.zip">DOWNLOAD</a>
            </body></html>
            """;

        var detail = ModHubService.ParseDetailPage(html, 352048, "https://ex/detail");
        detail.Title.Should().Be("Solek");
        detail.Author.Should().Be("RajotGPLAY");
        detail.Category.Should().Be("Europäische Karten");
        detail.Version.Should().Be("1.1.0.0");
        detail.SizeText.Should().Be("1225.69 MB");
        detail.ReleaseDate.Should().Be("19.06.2026");
        detail.Platform.Should().Contain("PC/MAC");
        detail.Filename.Should().Be("FS25_Solek.zip");
        detail.RatingText.Should().StartWith("4.5");
        detail.DescriptionText.Should().Contain("Ein tolle Karte");
        detail.DescriptionText.Should().Contain("Changelog");
        detail.ScreenshotUrls.Should().HaveCount(2); // iconBig ausgefiltert
        detail.ScreenshotUrls[0].Should().Contain("screenshot0");
        detail.DownloadUrl.Should().Contain("FS25_Solek.zip");
    }

    [Fact]
    public void ExtractDownloadUrl_FindetPassendeZipZurModId()
    {
        // Detail-Seiten listen die eigene ZIP + oft „ähnliche Mods" mit anderen IDs.
        var html = """
            <html><body>
              <a href="https://cdn27.giants-software.com/modHub/storage/00352048/FS25_Solek.zip" class="button">JETZT HERUNTERLADEN</a>
              <ul>
                <li><a href="https://cdn27.giants-software.com/modHub/storage/00325278/FS25_FarmerSetBuilding.zip">Farm Buildings</a></li>
                <li><a href="https://cdn27.giants-software.com/modHub/storage/00335998/FS25_WhiteModernFarmSet.zip">White Brick Farm</a></li>
              </ul>
            </body></html>
            """;

        var url = ModHubService.ExtractDownloadUrl(html, 352048);
        url.Should().Be("https://cdn27.giants-software.com/modHub/storage/00352048/FS25_Solek.zip");
    }

    [Fact]
    public void ExtractDownloadUrl_LiefertNull_WennKeinLinkVorhanden()
    {
        var html = "<html><body>Kein Download-Link hier</body></html>";
        ModHubService.ExtractDownloadUrl(html, 999).Should().BeNull();
    }

    [Fact]
    public void ExtractDownloadUrl_IgnoriertFremdeModIdsWennEigeneNichtVorhanden()
    {
        // Nur Empfehlungen, keine ZIP zur angefragten ID → null (nicht fälschlich Fremd-ZIP).
        var html = """
            <html><body>
              <a href="https://cdn27.giants-software.com/modHub/storage/00111111/other.zip">A</a>
              <a href="https://cdn27.giants-software.com/modHub/storage/00222222/other2.zip">B</a>
            </body></html>
            """;
        ModHubService.ExtractDownloadUrl(html, 352048).Should().BeNull();
    }

    [Fact]
    public void ParseCategories_ExtrahiertFilterKeysUndLabels()
    {
        var html = """
            <html><body>
              <a class="menu-link" href="mods.php?title=fs2025&filter=latest&page=0">NEUESTE</a>
              <a class="menu-link" href="mods.php?title=fs2025&filter=mapEurope&page=0">Europäische Karten</a>
              <a class="menu-link" href="mods.php?title=fs2025&filter=mapEurope&page=0">Europäische Karten</a>
              <a class="menu-link" href="mods.php?title=fs2025&filter=tractorsL&page=0">Großtraktoren</a>
              <a href="mods.php?title=fs2025&page=0">Ohne Filter</a>
            </body></html>
            """;
        var cats = ModHubService.ParseCategories(html);
        cats.Should().HaveCount(3);
        cats.Should().Contain(c => c.Filter == "mapEurope" && c.Label == "Europäische Karten");
        cats.Should().Contain(c => c.Filter == "tractorsL" && c.Label == "Großtraktoren");
    }

    [Fact]
    public void BuildListUrl_MitFilter_HaengtFilterAn()
    {
        var url = ModHubServiceTestHelper.BuildListUrl(1, "de", "mapEurope");
        url.Should().Contain("filter=mapEurope");
    }

    [Fact]
    public void ParseListPage_LegacyFallback_WennCardStrukturFehltAberAnkerVorhandenSind()
    {
        // Kein .machines--mods vorhanden, nur nackte mod_id-Links.
        var html = """
            <html><body>
              <a href="mod.php?mod_id=999&title=fs2025">Mod-Link</a>
            </body></html>
            """;
        var entries = ModHubService.ParseListPage(html);
        entries.Should().ContainSingle();
        entries[0].DetailUrl.Should().Contain("mod_id=999");
    }
}

/// <summary>Zugriff auf die internal-URL-Builder-Methode aus Test-Assembly.</summary>
internal static class ModHubServiceTestHelper
{
    public static string BuildListUrl(int page, string lang, string? filter = null)
    {
        var mi = typeof(ModHubService).GetMethod("BuildListUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)mi.Invoke(null, new object?[] { page, lang, filter })!;
    }
}
