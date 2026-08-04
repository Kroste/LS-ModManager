using FluentAssertions;
using LSModManager.Models;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

/// <summary>
/// Verifiziert den JSON-Parser für die Modhoster-Katalog-API. Aufbau der
/// Antwort ist an /mods.json?game_id=1 orientiert (LS25 bei modhoster).
/// </summary>
public sealed class ModhosterCatalogServiceTests
{
    [Fact]
    public void ParseCatalogJson_ExtrahiertModsMitAllenFeldern()
    {
        var json = """
            {
              "modifications": [
                {
                  "name": "Durchfahrtsschuppen",
                  "cached_slug": "drive-through-shed",
                  "game_name": "Landwirtschafts Simulator 25",
                  "thumb_url": "https://cdn.modhoster.com/system/files/thumb/x.webp",
                  "image_url": "https://cdn.modhoster.com/system/files/huge/x.webp",
                  "image": {
                    "urls": {
                      "shop": "https://cdn.modhoster.com/system/files/shop/x.webp",
                      "thumb": "https://cdn.modhoster.com/system/files/thumb/x.webp"
                    }
                  },
                  "user": { "id": 1, "name": "benjin123" }
                }
              ]
            }
            """;

        var entries = ModhosterCatalogService.ParseCatalogJson(json);
        entries.Should().ContainSingle();

        var e = entries[0];
        e.Title.Should().Be("Durchfahrtsschuppen");
        e.Author.Should().Be("benjin123");
        e.Category.Should().Be("Landwirtschafts Simulator 25");
        // shop > thumb > thumb_url — die shop-URL soll bevorzugt werden.
        e.PreviewUrl.Should().Be("https://cdn.modhoster.com/system/files/shop/x.webp");
        e.DetailUrl.Should().Be("https://www.modhoster.de/mods/drive-through-shed");
        e.Source.Should().Be(ModHubEntry.ModhosterSource);
        e.CanInAppDownload.Should().BeFalse();
    }

    [Fact]
    public void ParseCatalogJson_FallbackAufThumbUrl_WennKeinImageObject()
    {
        var json = """
            {
              "modifications": [
                {
                  "name": "Test",
                  "cached_slug": "test-slug",
                  "thumb_url": "https://cdn/thumb.webp"
                }
              ]
            }
            """;
        var entries = ModhosterCatalogService.ParseCatalogJson(json);
        entries[0].PreviewUrl.Should().Be("https://cdn/thumb.webp");
    }

    [Fact]
    public void ParseCatalogJson_LeereModifications_LiefertLeereListe()
    {
        ModhosterCatalogService.ParseCatalogJson("""{"modifications":[]}""")
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseCatalogJson_SkipptEintraegeOhneSlugOderName()
    {
        var json = """
            {
              "modifications": [
                { "name": "OK", "cached_slug": "ok" },
                { "name": "Kein Slug" },
                { "cached_slug": "kein-name" }
              ]
            }
            """;
        var entries = ModhosterCatalogService.ParseCatalogJson(json);
        entries.Should().ContainSingle();
        entries[0].Title.Should().Be("OK");
    }
}
