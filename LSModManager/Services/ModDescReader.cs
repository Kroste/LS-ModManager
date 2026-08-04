using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Liest <c>modDesc.xml</c> aus einer LS/FS-Mod-ZIP und extrahiert die Metadaten
/// plus optional ein Vorschau-PNG. LS25-Mods verwenden meist <c>icon.dds</c> —
/// das kann Avalonia nicht nativ zeichnen, deswegen versuchen wir zusätzlich
/// alternative PNG-Icons (icon.png, store_*.png) für die Vorschau zu finden.
/// </summary>
public sealed class ModDescReader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly string[] LanguagePreference = { "de", "en" };

    public ModReadResult Read(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var descEntry = archive.GetEntry("modDesc.xml");
            if (descEntry is null)
                return new ModReadResult(null, null, "modDesc.xml nicht gefunden");

            XDocument doc;
            using (var stream = descEntry.Open())
                doc = XDocument.Load(stream);

            var root = doc.Root ?? throw new InvalidDataException("modDesc.xml hat kein Root-Element");
            var metadata = ParseMetadata(root);
            var previewBytes = TryExtractPreview(archive, metadata.IconFileName);
            return new ModReadResult(metadata, previewBytes, null);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte modDesc.xml nicht lesen: {path}", zipPath);
            return new ModReadResult(null, null, ex.Message);
        }
    }

    private static ModMetadata ParseMetadata(XElement root)
    {
        var descVersion = int.TryParse((string?)root.Attribute("descVersion"), out var v) ? v : 0;
        var author = (string?)root.Element("author") ?? "";
        var version = (string?)root.Element("version") ?? "";
        var iconFile = (string?)root.Element("iconFilename");
        var multiplayer = string.Equals(
            (string?)root.Element("multiplayer")?.Attribute("supported"),
            "true", StringComparison.OrdinalIgnoreCase);

        var title = PickLocalized(root.Element("title")) ?? Path.GetFileNameWithoutExtension(iconFile ?? "");
        var description = PickLocalized(root.Element("description")) ?? "";

        return new ModMetadata(
            Title: title.Trim(),
            Author: author.Trim(),
            Version: version.Trim(),
            Description: description.Trim(),
            IconFileName: string.IsNullOrWhiteSpace(iconFile) ? null : iconFile,
            MultiplayerSupported: multiplayer,
            DescVersion: descVersion);
    }

    /// <summary>DE, dann EN, dann erstes vorhandenes Kind, dann Text-Value.</summary>
    private static string? PickLocalized(XElement? node)
    {
        if (node is null) return null;
        foreach (var lang in LanguagePreference)
        {
            var e = node.Element(lang);
            if (e is not null && !string.IsNullOrWhiteSpace(e.Value))
                return e.Value;
        }
        var first = node.Elements().FirstOrDefault();
        if (first is not null && !string.IsNullOrWhiteSpace(first.Value))
            return first.Value;
        return string.IsNullOrWhiteSpace(node.Value) ? null : node.Value;
    }

    /// <summary>
    /// Sucht ein Vorschau-PNG in der ZIP. Reihenfolge:
    /// 1. iconFilename mit .png (statt .dds), 2. icon.png, 3. store_*.png, 4. beliebiges *.png.
    /// DDS wird bewusst ausgelassen — Avalonia kann das nicht rendern.
    /// </summary>
    private static byte[]? TryExtractPreview(ZipArchive archive, string? iconFileName)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(iconFileName))
        {
            var withoutExt = Path.GetFileNameWithoutExtension(iconFileName);
            candidates.Add(withoutExt + ".png");
            candidates.Add(iconFileName); // falls Nutzer PNG angibt
        }
        candidates.Add("icon.png");

        foreach (var name in candidates)
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                return ReadBytes(entry);
        }

        var store = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("store_", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        if (store is not null) return ReadBytes(store);

        var anyPng = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        return anyPng is null ? null : ReadBytes(anyPng);
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var ms = new MemoryStream();
        using var s = entry.Open();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>Ergebnis eines ZIP-Lesens: Metadaten + optionale Vorschau + Fehler.</summary>
public sealed record ModReadResult(
    ModMetadata? Metadata,
    byte[]? PreviewPngBytes,
    string? Error);
