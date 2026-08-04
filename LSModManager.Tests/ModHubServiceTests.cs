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
    public void BuildListUrl_HaengtPageAbSeite2An()
    {
        var url = ModHubServiceTestHelper.BuildListUrl(3, "en");
        url.Should().Contain("page=3");
        url.Should().Contain("lang=en");
    }

    [Fact]
    public void ParseListPage_FindetAnkerMitModId()
    {
        // Vereinfachte Katalog-HTML (Struktur weicht bei GIANTS im Detail ab).
        var html = """
            <html><body>
              <div class="mod-item">
                <a href="/mod.php?mod_id=12345&title=fs2025">
                  <img src="/preview/12345.png" alt="Super-Traktor">
                  Super-Traktor
                </a>
                <span class="author">Kroste</span>
                <span class="category">Traktoren</span>
              </div>
              <div class="mod-item">
                <a href="mod.php?mod_id=67890&title=fs2025" title="Weizen-Mod">
                  <img data-src="https://cdn.example/pic.jpg" alt="">
                </a>
              </div>
            </body></html>
            """;

        var entries = ModHubService.ParseListPage(html);
        entries.Should().HaveCount(2);
        entries[0].Title.Should().Contain("Super-Traktor");
        entries[0].PreviewUrl.Should().Contain("preview/12345.png");
        entries[0].DetailUrl.Should().Contain("mod_id=12345");
        entries[0].Author.Should().Be("Kroste");
        entries[0].Category.Should().Be("Traktoren");
        entries[1].Title.Should().Be("Weizen-Mod");
        entries[1].PreviewUrl.Should().Be("https://cdn.example/pic.jpg");
    }

    [Fact]
    public void ParseListPage_IgnoriertDoppelteModIds()
    {
        var html = """
            <html><body>
              <a href="/mod.php?mod_id=1&title=fs2025">A</a>
              <a href="/mod.php?mod_id=1&extra=x">B</a>
              <a href="/mod.php?mod_id=2&title=fs2025">C</a>
            </body></html>
            """;
        var entries = ModHubService.ParseListPage(html);
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void ParseListPage_LeeresHtml_LiefertLeereListe()
    {
        var entries = ModHubService.ParseListPage("<html><body>Nichts hier</body></html>");
        entries.Should().BeEmpty();
    }
}

/// <summary>Zugriff auf die internal-URL-Builder-Methode aus Test-Assembly.</summary>
internal static class ModHubServiceTestHelper
{
    public static string BuildListUrl(int page, string lang)
    {
        var mi = typeof(ModHubService).GetMethod("BuildListUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)mi.Invoke(null, new object[] { page, lang })!;
    }
}
