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
            string? previewPath = null;
            if (readResult.PreviewPngBytes is { Length: > 0 })
                previewPath = CachePreview(file, readResult.PreviewPngBytes);

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

        var read = _reader.Read(destination);
        var info = new FileInfo(destination);
        string? previewPath = null;
        if (read.PreviewPngBytes is { Length: > 0 })
            previewPath = CachePreview(destination, read.PreviewPngBytes);
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
    /// Schreibt das extrahierte Preview-PNG in einen Cache-Ordner. Der Pfad ist
    /// stabil pro Mod-Datei (SHA-freier Ansatz: FileName + Length). Das entlastet
    /// die UI, weil Avalonia den Bitmap direkt vom Dateipfad laden kann.
    /// </summary>
    private static string CachePreview(string modFilePath, byte[] pngBytes)
    {
        var cacheDir = Path.Combine(GetCacheRoot(), "previews");
        Directory.CreateDirectory(cacheDir);
        var name = Path.GetFileNameWithoutExtension(modFilePath) + ".png";
        var target = Path.Combine(cacheDir, name);
        try
        {
            File.WriteAllBytes(target, pngBytes);
            return target;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Preview nicht cachen: {p}", target);
            return target;
        }
    }

    private static string GetCacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LSModManager", "cache");
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(xdg))
            xdg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        return Path.Combine(xdg, "LSModManager");
    }
}
