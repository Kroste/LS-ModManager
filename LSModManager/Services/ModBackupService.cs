using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Erstellt und liest Backup-Archive der aktuellen Mod-Konfiguration.
///
/// <para><b>Format:</b> ein ZIP-Archiv mit <c>manifest.json</c> und einem
/// <c>mods/</c>-Unterordner, der alle Mod-ZIPs im Original enthält (auch
/// deaktivierte — Endung im Manifest markiert). Damit lässt sich der Zustand
/// des Mod-Ordners exakt rekonstruieren, ohne dass beim Restore Internet nötig
/// wäre oder ein Mod aus dem Katalog verschwunden sein darf.</para>
///
/// <para><b>Manifest-Versionierung:</b> <see cref="BackupManifest.Version"/>
/// steht auf 1. Bei Format-Änderungen inkrementieren und beim Restore anhand
/// dieser Zahl migrieren — die Deserializierung soll fehlschlagen wenn eine
/// unbekannte Version reinkommt (das schützt den User vor Datenverlust).</para>
/// </summary>
public sealed class ModBackupService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Format-Version. Wird ins Manifest geschrieben und beim Restore geprüft.</summary>
    public const int CurrentFormatVersion = 1;

    private const string ManifestEntryName = "manifest.json";
    private const string ModsFolderPrefix = "mods/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ModPathService _paths;
    private readonly ModInstallService _install;
    private readonly ModDescReader _reader;

    public ModBackupService(ModPathService paths, ModInstallService install, ModDescReader reader)
    {
        _paths = paths;
        _install = install;
        _reader = reader;
    }

    /// <summary>
    /// Bündelt alle Mods aus dem konfigurierten Mod-Ordner (aktiv + deaktiviert)
    /// in ein ZIP-Archiv. Progress läuft von 0..1 über die Anzahl der Mods.
    /// </summary>
    public async Task<BackupResult> CreateBackupAsync(
        string targetZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var mods = _install.ListInstalled();
        if (mods.Count == 0)
            throw new InvalidOperationException("Keine installierten Mods vorhanden — nichts zu sichern.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetZipPath)!);

        // In neue Temp-Datei schreiben und am Ende umbenennen — verhindert einen
        // halbgeschriebenen Backup-ZIP wenn die App zwischendurch crasht.
        var tmpPath = targetZipPath + ".tmp";
        if (File.Exists(tmpPath)) File.Delete(tmpPath);

        var manifest = new BackupManifest(
            Version: CurrentFormatVersion,
            CreatedUtc: DateTime.UtcNow,
            AppVersion: typeof(ModBackupService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Mods: mods.Select(m => new BackupManifestEntry(
                FileName: m.FileName,
                IsEnabled: m.IsEnabled,
                ModVersion: m.Metadata?.Version,
                Author: m.Metadata?.Author,
                Title: m.Metadata?.Title)).ToList());

        await Task.Run(() =>
        {
            using var fs = File.Create(tmpPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // Manifest zuerst — dann kann ein späterer Reader es schnell finden.
            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

            for (var i = 0; i < mods.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var mod = mods[i];
                // Mod-ZIPs sind bereits komprimiert — CompressionLevel.NoCompression
                // spart ~30% CPU bei minimalem Größenzuwachs (ZIP-in-ZIP komprimiert
                // sich sowieso kaum).
                var modEntry = archive.CreateEntry(ModsFolderPrefix + mod.FileName,
                    CompressionLevel.NoCompression);
                using (var entryStream = modEntry.Open())
                using (var srcStream = File.OpenRead(mod.FilePath))
                    srcStream.CopyTo(entryStream);
                progress?.Report(new BackupProgress(i + 1, mods.Count, mod.FileName));
            }
        }, ct).ConfigureAwait(false);

        // Atomic move: das fertige ZIP ersetzt eine ggf. vorhandene Vorgängerdatei
        // erst wenn wir sicher wissen dass die neue Version komplett geschrieben ist.
        if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
        File.Move(tmpPath, targetZipPath);

        var fileInfo = new FileInfo(targetZipPath);
        Log.Info("Backup erstellt: {p} ({n} Mods, {size} Bytes)",
            targetZipPath, mods.Count, fileInfo.Length);
        return new BackupResult(targetZipPath, mods.Count, fileInfo.Length);
    }

    /// <summary>
    /// Liest das Manifest aus einem Backup-ZIP, ohne die Mod-ZIPs zu entpacken.
    /// Nützlich für Restore-Preview oder Backup-Inspektion. Wirft bei fehlendem
    /// Manifest oder unbekannter Format-Version.
    /// </summary>
    public static BackupManifest ReadManifest(string backupZipPath)
    {
        using var archive = ZipFile.OpenRead(backupZipPath);
        var entry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException($"Backup enthält kein {ManifestEntryName}.");
        using var reader = new StreamReader(entry.Open());
        var manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidDataException("Manifest ist leer oder nicht lesbar.");
        if (manifest.Version != CurrentFormatVersion)
            throw new InvalidDataException(
                $"Unbekannte Backup-Format-Version: {manifest.Version} (App unterstützt {CurrentFormatVersion}).");
        return manifest;
    }

    /// <summary>
    /// Stellt alle Mods aus einem Backup-ZIP im aktuellen Mod-Ordner wieder her.
    /// Enabled-State wird aus dem Manifest übernommen. Bereits vorhandene Mods
    /// werden überschrieben (Log-Warnung). Progress läuft über die Anzahl der Mods.
    /// </summary>
    public async Task<RestoreResult> RestoreBackupAsync(
        string backupZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException("Backup-Datei existiert nicht", backupZipPath);

        var modPath = _paths.GetModPath()
            ?? throw new InvalidOperationException("Mod-Pfad nicht konfiguriert — in Einstellungen setzen.");
        Directory.CreateDirectory(modPath);

        var manifest = ReadManifest(backupZipPath);
        var restored = 0;
        var skipped = 0;

        // Temp-Ordner pro Restore — sauber isoliert, wird am Ende gelöscht.
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LSModManager-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(backupZipPath);
                for (var i = 0; i < manifest.Mods.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var meta = manifest.Mods[i];
                    var entry = archive.GetEntry(ModsFolderPrefix + meta.FileName);
                    if (entry is null)
                    {
                        Log.Warn("Restore: Mod-ZIP fehlt im Backup: {n}", meta.FileName);
                        skipped++;
                        progress?.Report(new BackupProgress(i + 1, manifest.Mods.Count, meta.FileName));
                        continue;
                    }

                    // Filename beim Extract IMMER auf .zip normalisieren — Install
                    // übernimmt den Dateinamen 1:1 aus der Quelle und würde einen
                    // .zip.disabled-Entry als .zip.disabled kopieren. SetEnabled(false)
                    // hängt danach nochmal .disabled an → .zip.disabled.disabled (Bug!).
                    // Der ursprüngliche Enabled-State steht ohnehin im Manifest.
                    var normalizedName = meta.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? meta.FileName.Substring(0, meta.FileName.Length - ".disabled".Length)
                        : meta.FileName;
                    var tmpZip = Path.Combine(tmpDir, normalizedName);
                    entry.ExtractToFile(tmpZip, overwrite: true);

                    try
                    {
                        var installed = _install.Install(tmpZip, overwrite: true);
                        if (!meta.IsEnabled)
                            _install.SetEnabled(installed, enabled: false);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Restore-Install übersprungen: {n}", meta.FileName);
                        skipped++;
                    }
                    finally
                    {
                        try { File.Delete(tmpZip); } catch { /* best-effort */ }
                    }

                    progress?.Report(new BackupProgress(i + 1, manifest.Mods.Count, meta.FileName));
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }
        }

        Log.Info("Restore fertig: {r} wiederhergestellt, {s} übersprungen", restored, skipped);
        return new RestoreResult(restored, skipped, manifest);
    }
}

/// <summary>Backup-Manifest-Wurzel. Serialisiert nach <c>manifest.json</c>.</summary>
public sealed record BackupManifest(
    int Version,
    DateTime CreatedUtc,
    string AppVersion,
    List<BackupManifestEntry> Mods);

/// <summary>Eintrag pro Mod im Manifest.</summary>
public sealed record BackupManifestEntry(
    string FileName,
    bool IsEnabled,
    string? ModVersion,
    string? Author,
    string? Title);

/// <summary>Fortschritt für Backup und Restore.</summary>
public sealed record BackupProgress(int Current, int Total, string CurrentFileName)
{
    public double Fraction => Total == 0 ? 0 : (double)Current / Total;
}

public sealed record BackupResult(string FilePath, int ModCount, long FileSizeBytes);

public sealed record RestoreResult(int RestoredCount, int SkippedCount, BackupManifest Manifest);
