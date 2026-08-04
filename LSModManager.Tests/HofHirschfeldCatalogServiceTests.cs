using FluentAssertions;
using LSModManager.Models;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

public sealed class HofHirschfeldCatalogServiceTests
{
    [Fact]
    public void ParseCategorySlugs_ExtrahiertAlleKategorienUniqEinmal()
    {
        var html = """
            <nav>
              <a href="https://www.hof-hirschfeld.de/category/traktoren">Traktoren</a>
              <a href="https://www.hof-hirschfeld.de/category/lkw">LKW</a>
              <a href="https://www.hof-hirschfeld.de/category/traktoren">Traktoren (Duplikat)</a>
              <a href="https://www.hof-hirschfeld.de/anderes">Nicht-Kategorie</a>
            </nav>
            """;
        var slugs = HofHirschfeldCatalogService.ParseCategorySlugs(html);
        slugs.Should().BeEquivalentTo("traktoren", "lkw");
    }

    [Fact]
    public void ParseCategoryPage_ExtrahiertModsAusKarten()
    {
        var html = """
            <html><body>
              <div>
                <a class="mod-card__media" href="https://www.hof-hirschfeld.de/mod/fendt-1050-hirschfeld-version-fuer-ls25">
                  <img src="https://www.hof-hirschfeld.de/assets/uploads/2026/04/fendt.png" alt="Fendt 1050 Hirschfeld-Version für LS25">
                </a>
                <a class="mod-card__media" href="/mod/john-deere-7810">
                  <img src="/assets/uploads/jd.png" alt="John Deere 7810">
                </a>
              </div>
            </body></html>
            """;
        var entries = HofHirschfeldCatalogService.ParseCategoryPage(html, "traktoren");
        entries.Should().HaveCount(2);

        entries[0].Title.Should().Be("Fendt 1050 Hirschfeld-Version für LS25");
        entries[0].Author.Should().Be("Hof Hirschfeld");
        entries[0].DetailUrl.Should().Contain("/mod/fendt-1050-hirschfeld-version-fuer-ls25");
        entries[0].PreviewUrl.Should().StartWith("https://www.hof-hirschfeld.de");
        entries[0].Source.Should().Be(ModHubEntry.HofHirschfeldSource);
        entries[0].CanInAppDownload.Should().BeFalse();
        entries[0].Category.Should().Be("Traktoren");

        entries[1].Title.Should().Be("John Deere 7810");
        // Relativer href wird mit BaseUrl präfixiert:
        entries[1].DetailUrl.Should().StartWith("https://www.hof-hirschfeld.de/mod/");
        entries[1].PreviewUrl.Should().StartWith("https://www.hof-hirschfeld.de");
    }

    [Fact]
    public void ParseCategoryPage_DedupliziertBeiDoppelterUrl()
    {
        var html = """
            <a class="mod-card__media" href="/mod/foo"><img src="/a.png" alt="Foo"></a>
            <a class="mod-card__media" href="/mod/foo"><img src="/a.png" alt="Foo"></a>
            """;
        HofHirschfeldCatalogService.ParseCategoryPage(html, "test").Should().ContainSingle();
    }

    [Fact]
    public void ExtractMaxPage_LiefertHoechsteSeitenzahl()
    {
        var html = """
            <nav class="pagination">
              <a class="is-active" href="?page=1">1</a>
              <a href="?page=2">2</a>
              <a href="?page=3">3</a>
            </nav>
            """;
        HofHirschfeldCatalogService.ExtractMaxPage(html).Should().Be(3);
    }

    [Fact]
    public void ExtractMaxPage_KeinePagination_Liefert1()
    {
        HofHirschfeldCatalogService.ExtractMaxPage("<html><body>Keine Nav</body></html>")
            .Should().Be(1);
    }
}
