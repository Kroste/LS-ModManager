using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

/// <summary>
/// Tests für den DDS→PNG-Konverter. Wir generieren im Test ein minimales
/// unkomprimiertes BGRA-DDS (kein externes Fixture-File nötig — der DDS-
/// Header ist wohldefiniert und in ~60 Zeilen komplett aufgebaut), lassen
/// Pfim + SkiaSharp durchlaufen und prüfen dass ein valides PNG rauskommt.
/// </summary>
public sealed class DdsToPngConverterTests
{
    [Fact]
    public void Convert_UnkomprimiertesBgraDds_LiefertValidesPng()
    {
        // 2x2 Pixel, BGRA-uncompressed. Farben: rot, grün, blau, weiß.
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,   // (0,0) BGRA: rot
            0x00, 0xFF, 0x00, 0xFF,   // (1,0) BGRA: grün
            0xFF, 0x00, 0x00, 0xFF,   // (0,1) BGRA: blau
            0xFF, 0xFF, 0xFF, 0xFF,   // (1,1) BGRA: weiß
        };
        var dds = BuildUncompressedBgraDds(width: 2, height: 2, pixels);

        var png = DdsToPngConverter.Convert(dds);

        png.Should().NotBeNull();
        // PNG-Magic-Bytes prüfen — wenn Skia irgendwas anderes rausgibt, ist der Test kaputt.
        png![0].Should().Be(0x89);
        png[1].Should().Be(0x50);
        png[2].Should().Be(0x4E);
        png[3].Should().Be(0x47);
    }

    [Fact]
    public void Convert_ZuKurzerInput_LiefertNull()
    {
        // Weniger als ein DDS-Header (128 Bytes) — Konverter soll gar nicht erst versuchen.
        DdsToPngConverter.Convert(new byte[] { 0x44, 0x44, 0x53, 0x20 }).Should().BeNull();
    }

    [Fact]
    public void Convert_MuellDaten_LiefertNull()
    {
        // Header-Länge korrekt, Inhalt zufällig — Pfim wirft, Konverter fängt still.
        var garbage = new byte[256];
        for (var i = 0; i < garbage.Length; i++) garbage[i] = (byte)i;

        DdsToPngConverter.Convert(garbage).Should().BeNull();
    }

    // -- DDS-Builder (uncompressed BGRA/32bpp) ------------------------------
    //
    // DDS-File-Layout (Microsoft-Spec):
    //   4 B  Magic ("DDS ")
    //   124 B DDS_HEADER
    //     4 B dwSize (=124)
    //     4 B dwFlags (CAPS|HEIGHT|WIDTH|PIXELFORMAT|PITCH = 0x0000100F)
    //     4 B dwHeight
    //     4 B dwWidth
    //     4 B dwPitchOrLinearSize (Width * 4 für 32bpp uncompressed)
    //     4 B dwDepth
    //     4 B dwMipMapCount
    //    44 B dwReserved1[11]
    //    32 B DDS_PIXELFORMAT (dwSize=32, Flags=RGB|ALPHAPIXELS=0x41,
    //                          FourCC=0, RGBBitCount=32, BGRA-Bitmasks)
    //     4 B dwCaps (TEXTURE=0x1000)
    //     4 B dwCaps2/3/4 + Reserved2 (alles 0)
    //   ...  Pixeldaten

    internal static byte[] BuildUncompressedBgraDds(int width, int height, byte[] pixels)
    {
        var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);

        w.Write(new byte[] { 0x44, 0x44, 0x53, 0x20 }); // "DDS "

        w.Write(124);                    // dwSize
        w.Write(0x0000100F);             // dwFlags: CAPS|HEIGHT|WIDTH|PITCH|PIXELFORMAT
        w.Write(height);                 // dwHeight
        w.Write(width);                  // dwWidth
        w.Write(width * 4);              // dwPitchOrLinearSize
        w.Write(0);                      // dwDepth
        w.Write(0);                      // dwMipMapCount
        for (var i = 0; i < 11; i++) w.Write(0); // dwReserved1[11]

        // DDS_PIXELFORMAT: 32bpp BGRA uncompressed
        w.Write(32);                     // pf.dwSize
        w.Write(0x41);                   // pf.dwFlags: RGB(0x40) | ALPHAPIXELS(0x01)
        w.Write(0);                      // pf.dwFourCC
        w.Write(32);                     // pf.dwRGBBitCount
        w.Write(0x00FF0000u);            // R-Mask
        w.Write(0x0000FF00u);            // G-Mask
        w.Write(0x000000FFu);            // B-Mask
        w.Write(0xFF000000u);            // A-Mask

        w.Write(0x1000);                 // dwCaps: TEXTURE
        w.Write(0);                      // dwCaps2
        w.Write(0);                      // dwCaps3
        w.Write(0);                      // dwCaps4
        w.Write(0);                      // dwReserved2

        w.Write(pixels);
        return ms.ToArray();
    }
}
