using System.IO;
using System.IO.Compression;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Kern-Operationen für Mods im lokalen Mod-Ordner: Auflisten, Installieren,
/// Deinstallieren, Aktivieren/Deaktivieren (.zip.disabled-Suffix).
/// Alle Aufrufe protokollieren wir mit vollem Pfad — Datei-Manipulation ist
/// im User-Datenordner heikel (siehe pitfalls.md → Trend Micro Behavior).
/// </summary>
public sealed class ModInstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ModPathService _paths;
    private readonly ModDescReader _reader;

    public ModInstallService(ModPathService paths, ModDescReader reader)
    {
        _paths = paths;
        _reader = reader;
    }

    /// <summary>
    /// Liest alle Mods (.zip und .zip.disabled) aus dem Mod-Ordner.
    /// Fehlerhafte ZIPs werden mit <see cref="InstalledMod.ReadError"/> zurückgegeben,
    /// nicht komplett verschluckt — der Nutzer soll sehen, was defekt ist.
    /// </summary>
    public IReadOnlyList<InstalledMod> ListInstalled()
    {
        var path = _paths.GetModPath();
        if (path is null || !Directory.Exists(path))
        {
            Log.Info("Mod-Ordner existiert nicht: {p}", path ?? "<null>");
            return Array.Empty<InstalledMod>();
        }

        var result = new List<InstalledMod>();
        foreach (var file in Directory.EnumerateFiles(path))
        {
            var isZip = file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var isDisabled = file.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase);
            if (!isZip && !isDisabled) continue;

            var info = new FileInfo(file);
            var readResult = _reader.Read(file);
            var previewPath = ResolvePreview(file, readResult.PreviewPngBytes);

            result.Add(new InstalledMod(
                FilePath: file,
                FileName: Path.GetFileName(file),
                FileSizeBytes: info.Length,
                InstalledUtc: info.LastWriteTimeUtc,
                IsEnabled: isZip,
                Metadata: readResult.Metadata,
                PreviewImagePath: previewPath,
                ReadError: readResult.Error));
        }
        return result;
    }

    /// <summary>
    /// Kopiert eine Mod-ZIP in den Mod-Ordner. Wirft eine <see cref="InvalidDataException"/>
    /// wenn die Quelle keine gültige Mod-ZIP mit modDesc.xml ist.
    /// </summary>
    public InstalledMod Install(string sourceZipPath, bool overwrite = false)
    {
        if (!File.Exists(sourceZipPath))
            throw new FileNotFoundException("Mod-ZIP existiert nicht", sourceZipPath);
        if (!IsModZip(sourceZipPath))
            throw new InvalidDataException("Datei enthält keine modDesc.xml — keine gültige LS/FS-Mod");

        var target = _paths.GetModPath()
            ?? throw new InvalidOperationException("Mod-Pfad nicht konfiguriert");
        Directory.CreateDirectory(target);

        var fileName = Path.GetFileName(sourceZipPath);
        var destination = Path.Combine(target, fileName);
        if (File.Exists(destination) && !overwrite)
            throw new IOException($"Mod ist bereits installiert: {fileName}");

        File.Copy(sourceZipPath, destination, overwrite: true);
        Log.Info("Mod installiert: {name} → {p}", fileName, destination);

        // Preview-Cache vom Source-Namen zum Ziel-Namen übernehmen (dieselbe
        // Datei-Basis — Downloads/Installiert haben identische ZIP-Namen).
        var sourceCache = AppPaths.FindExistingPreview(sourceZipPath);
        var targetExisting = AppPaths.FindExistingPreview(destination);
        if (sourceCache is not null && targetExisting is null)
        {
            try
            {
                var targetCache = AppPaths.PreviewCacheBasePathFor(destination)
                    + Path.GetExtension(sourceCache);
                File.Copy(sourceCache, targetCache, overwrite: false);
            }
            catch (Exception ex) { Log.Debug(ex, "Preview-Copy übersprungen"); }
        }

        var read = _reader.Read(destination);
        var info = new FileInfo(destination);
        var previewPath = ResolvePreview(destination, read.PreviewPngBytes);
        return new InstalledMod(destination, fileName, info.Length, info.LastWriteTimeUtc,
            IsEnabled: true, Metadata: read.Metadata, PreviewImagePath: previewPath,
            ReadError: read.Error);
    }

    /// <summary>Löscht die Mod-Datei aus dem Mod-Ordner.</summary>
    public void Uninstall(InstalledMod mod)
    {
        if (!File.Exists(mod.FilePath))
        {
            Log.Warn("Deinstallation: Datei bereits weg: {p}", mod.FilePath);
            return;
        }
        File.Delete(mod.FilePath);
        Log.Info("Mod deinstalliert: {p}", mod.FilePath);
    }

    /// <summary>
    /// Deaktiviert einen Mod (Endung .zip.disabled). LS25 ignoriert Dateien, die
    /// nicht auf .zip enden — so bleibt der Mod im Ordner, wird aber nicht geladen.
    /// </summary>
    public InstalledMod SetEnabled(InstalledMod mod, bool enabled)
    {
        if (mod.IsEnabled == enabled) return mod;

        var current = mod.FilePath;
        var target = enabled
            ? current.Substring(0, current.Length - ".disabled".Length)
            : current + ".disabled";

        if (File.Exists(target))
            throw new IOException($"Zieldatei existiert bereits: {target}");

        File.Move(current, target);
        Log.Info("Mod {state}: {p} → {t}", enabled ? "aktiviert" : "deaktiviert", current, target);

        return mod with { FilePath = target, FileName = Path.GetFileName(target), IsEnabled = enabled };
    }

    private static bool IsModZip(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.GetEntry("modDesc.xml") is not null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Datei ist keine lesbare ZIP: {p}", zipPath);
            return false;
        }
    }

    /// <summary>
    /// Listet alle ZIPs im persistenten Downloads-Ordner (heruntergeladen aber
    /// noch nicht installiert). Analog zu <see cref="ListInstalled"/>, aber ohne
    /// Enable/Disable — Downloads sind immer roh.
    /// </summary>
    public IReadOnlyList<InstalledMod> ListDownloaded()
    {
        var dir = AppPaths.DownloadsDir;
        var result = new List<InstalledMod>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.zip"))
        {
            var info = new FileInfo(file);
            var readResult = _reader.Read(file);
            var previewPath = ResolvePreview(file, readResult.PreviewPngBytes);

            result.Add(new InstalledMod(
                FilePath: file,
                FileName: Path.GetFileName(file),
                FileSizeBytes: info.Length,
                InstalledUtc: info.LastWriteTimeUtc,
                IsEnabled: true, // Downloads sind „aktiv" im Sinne von „bereit"
                Metadata: readResult.Metadata,
                PreviewImagePath: previewPath,
                ReadError: readResult.Error));
        }
        return result;
    }

    public void DeleteDownload(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Log.Warn("Download bereits weg: {p}", filePath);
            return;
        }
        // Sicherheitscheck: nur Dateien im Downloads-Ordner löschen dürfen.
        var normalized = Path.GetFullPath(filePath);
        if (!normalized.StartsWith(AppPaths.DownloadsDir, StringComparison.Ordinal))
            throw new InvalidOperationException("Datei liegt nicht im Downloads-Ordner");
        File.Delete(normalized);
        Log.Info("Download gelöscht: {p}", normalized);
    }

    /// <summary>
    /// Reihenfolge fürs Preview-Bild:
    /// 1. Wenn <see cref="ModDescReader"/> PNG-Bytes aus der ZIP extrahiert hat → cachen und nutzen.
    /// 2. Wenn schon ein Cache-Bild existiert (z.B. vom ModHub-Cover) → dieses nutzen.
    /// 3. Sonst null → UI zeigt Fallback-Emoji.
    /// </summary>
    private static string? ResolvePreview(string modFilePath, byte[]? pngBytes)
    {
        if (pngBytes is { Length: > 0 })
        {
            var ext = AppPaths.GuessImageExtension(pngBytes);
            // Sicherheitsnetz: kein Bild-Format erkannt → NICHT schreiben, sonst
            // landen DDS-Bytes o.ä. als .bin im Cache und Auto-Delete kickt in
            // Endlosschleife.
            if (ext != ".bin")
            {
                var target = AppPaths.PreviewCacheBasePathFor(modFilePath) + ext;
                try
                {
                    File.WriteAllBytes(target, pngBytes);
                    return target;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Konnte Preview nicht cachen: {p}", target);
                }
            }
        }
        return AppPaths.FindExistingPreview(modFilePath);
    }
}
