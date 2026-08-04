using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

public sealed class ModDescReaderTests : IDisposable
{
    private readonly string _tempDir;

    public ModDescReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lsmm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Read_ExtrahiertMetadatenAusModDesc()
    {
        var zip = CreateModZip("mymod.zip",
            modDesc: """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="86">
                    <author>Kroste Tester</author>
                    <version>1.2.3</version>
                    <title><de>Testmod DE</de><en>Testmod EN</en></title>
                    <description><de>Deutsche Beschreibung</de><en>English text</en></description>
                    <iconFilename>icon.dds</iconFilename>
                    <multiplayer supported="true" />
                </modDesc>
                """);

        var reader = new ModDescReader();
        var result = reader.Read(zip);

        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Author.Should().Be("Kroste Tester");
        result.Metadata.Version.Should().Be("1.2.3");
        result.Metadata.Title.Should().Be("Testmod DE"); // DE-Vorrang
        result.Metadata.Description.Should().Be("Deutsche Beschreibung");
        result.Metadata.IconFileName.Should().Be("icon.dds");
        result.Metadata.MultiplayerSupported.Should().BeTrue();
        result.Metadata.DescVersion.Should().Be(86);
    }

    [Fact]
    public void Read_FallbackAufEnglisch_WennKeinDeutschVorhanden()
    {
        var zip = CreateModZip("nur-en.zip",
            modDesc: """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="86">
                    <author>Anon</author>
                    <version>1.0</version>
                    <title><en>Only English</en></title>
                </modDesc>
                """);

        var result = new ModDescReader().Read(zip);
        result.Metadata!.Title.Should().Be("Only English");
    }

    [Fact]
    public void Read_ExtrahiertStorePngAlsPreview_WennKeinIconPng()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG-Magic
        var zip = CreateModZip("with-store.zip",
            modDesc: """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="86">
                    <author>A</author><version>1</version>
                    <title><de>t</de></title>
                    <iconFilename>icon.dds</iconFilename>
                </modDesc>
                """,
            extraFiles: new Dictionary<string, byte[]> { ["store_tractor.png"] = pngBytes });

        var result = new ModDescReader().Read(zip);
        result.PreviewPngBytes.Should().NotBeNull();
        result.PreviewPngBytes!.Should().BeEquivalentTo(pngBytes);
    }

    [Fact]
    public void Read_DekodiertDdsPreview_WennKeineAlternative()
    {
        // Mod hat nur icon.dds — kein PNG, kein Store-Bild. Vorher: kein Preview
        // (Emoji-Fallback). Nach DDS-Decoder-Integration: sollte ein echtes PNG
        // aus dem DDS rauskommen.
        var dds = BuildUncompressedBgraDdsFixture(width: 4, height: 4);
        var zip = CreateModZip("dds-only.zip",
            modDesc: """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="86">
                    <author>Kroste</author>
                    <version>1.0.0</version>
                    <title><en>DDS-only Mod</en></title>
                    <iconFilename>icon.dds</iconFilename>
                </modDesc>
                """,
            extraFiles: new Dictionary<string, byte[]> { { "icon.dds", dds } });

        var result = new ModDescReader().Read(zip);

        result.Error.Should().BeNull();
        result.PreviewPngBytes.Should().NotBeNull();
        // Magic-Bytes: es ist wirklich PNG (nicht durchgereichte DDS-Bytes).
        result.PreviewPngBytes![0].Should().Be(0x89);
        result.PreviewPngBytes[1].Should().Be(0x50);
    }

    [Fact]
    public void Read_BevorzugtPngUeberDds_WennBeideVorhanden()
    {
        // PNG ist immer besser — Store-Bilder sind kuratiert, DDS ist typisch
        // das kleine In-Game-Icon.
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG-Magic
            // Minimales aber gültiges IHDR (1x1 grayscale) — reicht damit IsPngOrJpeg passt.
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x00, 0x00, 0x00, 0x00,
        };
        var dds = BuildUncompressedBgraDdsFixture(4, 4);
        var zip = CreateModZip("both.zip",
            modDesc: """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="86">
                    <author>K</author><version>1</version>
                    <title><en>Both</en></title>
                    <iconFilename>icon.dds</iconFilename>
                </modDesc>
                """,
            extraFiles: new Dictionary<string, byte[]>
            {
                { "icon.png", pngBytes },
                { "icon.dds", dds },
            });

        var result = new ModDescReader().Read(zip);

        // Größe = pngBytes.Length wenn PNG genommen wurde. Nach DDS-Decoding
        // wäre das PNG deutlich größer (mindestens IHDR + IDAT + IEND für 4x4).
        result.PreviewPngBytes.Should().NotBeNull();
        result.PreviewPngBytes!.Length.Should().Be(pngBytes.Length);
    }

    [Fact]
    public void Read_MeldetFehler_WennModDescFehlt()
    {
        var zip = Path.Combine(_tempDir, "kein-desc.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("no modDesc");
        }

        var result = new ModDescReader().Read(zip);
        result.Metadata.Should().BeNull();
        result.Error.Should().Contain("modDesc.xml");
    }

    /// <summary>
    /// Minimales unkomprimiertes BGRA-DDS mit konstanten Pixeln — reicht für
    /// den DDS-Fallback-Test in <see cref="ModDescReader"/>. Delegiert an den
    /// DDS-Header-Builder aus <see cref="DdsToPngConverterTests"/> (Layout
    /// steht dort dokumentiert).
    /// </summary>
    private static byte[] BuildUncompressedBgraDdsFixture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = 0x40; // B
            pixels[i + 1] = 0x80; // G
            pixels[i + 2] = 0xC0; // R
            pixels[i + 3] = 0xFF; // A
        }
        return DdsToPngConverterTests.BuildUncompressedBgraDds(width, height, pixels);
    }

    private string CreateModZip(string name, string modDesc, Dictionary<string, byte[]>? extraFiles = null)
    {
        var path = Path.Combine(_tempDir, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var desc = archive.CreateEntry("modDesc.xml");
        using (var s = new StreamWriter(desc.Open(), Encoding.UTF8))
            s.Write(modDesc);

        if (extraFiles is not null)
        {
            foreach (var (fileName, bytes) in extraFiles)
            {
                var e = archive.CreateEntry(fileName);
                using var s = e.Open();
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return path;
    }
}
