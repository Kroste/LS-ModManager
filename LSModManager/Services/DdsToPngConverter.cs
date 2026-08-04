using System.IO;
using System.Runtime.InteropServices;
using NLog;
using Pfim;
using SkiaSharp;
using PfimImageFormat = Pfim.ImageFormat;

namespace LSModManager.Services;

/// <summary>
/// Konvertiert DDS-Bytes (LS/FS-Mod-Icons) in PNG-Bytes für den Preview-Cache.
///
/// <para>Hintergrund: Avalonia/Skia können DDS nicht nativ rendern. Viele LS25-
/// Mods (v.a. Community-Sachen ohne Store-Auftritt) haben aber nur eine
/// <c>icon.dds</c> in der ZIP — bisher fielen die auf den 🚜-Emoji-Fallback
/// zurück. Pfim dekodiert BC1/BC2/BC3-komprimierte sowie unkomprimierte
/// DDS-Formate zu rohen Pixel-Bytes, SkiaSharp encodet die dann als PNG.</para>
///
/// <para>Stride-Falle: Pfim gibt <see cref="IImage.Stride"/> zurück, das je
/// nach Format vom naiven <c>Width * BytesPerPixel</c> abweichen kann (Padding
/// auf Alignment-Grenzen). Beim Übergabe an SkiaSharp muss der echte Stride
/// verwendet werden, sonst gibt es sheared/verschobene Bilder.</para>
/// </summary>
public static class DdsToPngConverter
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Konvertiert DDS-Bytes zu PNG-Bytes. Gibt <c>null</c> zurück wenn das
    /// Format nicht unterstützt wird oder das Dekodieren fehlschlägt — Aufrufer
    /// soll dann still auf den Emoji-Fallback zurückfallen, nicht die App
    /// crashen.
    /// </summary>
    public static byte[]? Convert(byte[] ddsBytes)
    {
        if (ddsBytes.Length < 128) return null; // DDS-Header allein ist 128 Bytes

        try
        {
            using var stream = new MemoryStream(ddsBytes);
            using var image = Pfimage.FromStream(stream);
            return ToPng(image);
        }
        catch (Exception ex)
        {
            // Kein Warn — DDS-Varianten die Pfim nicht kennt sind normal, das
            // soll nicht das Log fluten. Debug reicht für Diagnose.
            Log.Debug(ex, "DDS-Dekodierung fehlgeschlagen (Format nicht unterstützt).");
            return null;
        }
    }

    private static byte[]? ToPng(IImage image)
    {
        var colorType = image.Format switch
        {
            // DDS legt Pixel in BGRA-Reihenfolge ab (nicht RGBA) — SKColorType
            // muss das matchen sonst kommen rote und blaue Kanäle vertauscht raus.
            PfimImageFormat.Rgba32 => SKColorType.Bgra8888,
            PfimImageFormat.Rgb24 => SKColorType.Bgra8888, // wird unten auf 32bpp expandiert
            _ => (SKColorType?)null,
        };
        if (colorType is null)
        {
            Log.Debug("Pfim-Format nicht unterstützt: {f}", image.Format);
            return null;
        }

        // Rgb24 → auf Rgba32 aufbohren (SkiaSharp erwartet 4 Bytes pro Pixel
        // für Bgra8888). Alpha auf 0xFF (opak) setzen.
        var (pixelBytes, stride) = image.Format == PfimImageFormat.Rgb24
            ? ExpandRgbToBgra(image)
            : (image.Data, image.Stride);

        var info = new SKImageInfo(image.Width, image.Height, colorType.Value, SKAlphaType.Premul);
        using var bitmap = new SKBitmap();

        // GCHandle: SkiaSharp braucht einen unveränderlichen Pointer auf die
        // Pixel-Bytes. Nach InstallPixels kopiert SKBitmap intern nicht — wir
        // müssen die Pin-Handle halten bis die Encode fertig ist. GCHandle.Free
        // im finally.
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            var pinned = handle.AddrOfPinnedObject();
            if (!bitmap.InstallPixels(info, pinned, stride))
                return null;
            using var img = SKImage.FromBitmap(bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Png, quality: 90);
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Rgb24 (3 Bytes/Pixel, BGR) → Bgra8888 (4 Bytes/Pixel, BGRA mit A=255).
    /// Nötig weil SkiaSharp kein 24-bit-Bgr-Format hat und Bgra8888 der billigste
    /// Konvertierungspfad ist.
    /// </summary>
    private static (byte[] Bytes, int Stride) ExpandRgbToBgra(IImage image)
    {
        var newStride = image.Width * 4;
        var result = new byte[newStride * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            var srcRow = y * image.Stride;
            var dstRow = y * newStride;
            for (var x = 0; x < image.Width; x++)
            {
                var srcPx = srcRow + x * 3;
                var dstPx = dstRow + x * 4;
                result[dstPx + 0] = image.Data[srcPx + 0]; // B
                result[dstPx + 1] = image.Data[srcPx + 1]; // G
                result[dstPx + 2] = image.Data[srcPx + 2]; // R
                result[dstPx + 3] = 0xFF;                  // A
            }
        }
        return (result, newStride);
    }
}
