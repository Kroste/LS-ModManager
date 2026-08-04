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
