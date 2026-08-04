using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

/// <summary>
/// Round-Trip-Tests für <see cref="ModBackupService"/>: aus einem präparierten
/// Mod-Ordner ein Backup erzeugen, den Ordner leeren, Restore einspielen und
/// prüfen dass Dateien + Enabled-States identisch wiederhergestellt sind.
///
/// <para>Isolation: <c>XDG_CONFIG_HOME</c> und ein manueller
/// <see cref="Models.AppSettings.ModPathOverride"/> zeigen auf Temp-Verzeichnisse
/// (analog <see cref="AppSettingsBrokenBackupTests"/>) — der Test rührt die
/// echte User-Config nie an. Nur Linux, weil XDG auf Windows keinen Effekt hat.</para>
/// </summary>
[Collection("EnvironmentIsolation")]
public sealed class ModBackupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _tempConfigDir;
    private readonly string _modDir;
    private readonly string? _originalXdg;

    public ModBackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "lsmm-backup-" + Guid.NewGuid().ToString("N"));
        _tempConfigDir = Path.Combine(_tempRoot, "config");
        _modDir = Path.Combine(_tempRoot, "mods");
        Directory.CreateDirectory(Path.Combine(_tempConfigDir, "LSModManager"));
        Directory.CreateDirectory(_modDir);

        _originalXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdg);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task RoundTrip_BackupUndRestore_StelltDateienUndEnabledStateWiederHer()
    {
        if (!OperatingSystem.IsLinux()) return; // siehe Klassen-Doku

        // 1) Zwei Mods im Ordner: einer aktiv (.zip), einer deaktiviert (.zip.disabled).
        CreateModZip(Path.Combine(_modDir, "ActiveMod.zip"), title: "Active", version: "1.0");
        CreateModZip(Path.Combine(_modDir, "DisabledMod.zip.disabled"), title: "Disabled", version: "2.0");

        var (backup, _) = BuildServices();
        var backupZip = Path.Combine(_tempRoot, "backup.zip");

        var ct = TestContext.Current.CancellationToken;
        var createResult = await backup.CreateBackupAsync(backupZip, ct: ct);
        createResult.ModCount.Should().Be(2);
        createResult.FilePath.Should().Be(backupZip);
        File.Exists(backupZip).Should().BeTrue();

        // 2) Mod-Ordner leerräumen — simuliert "auf neuem Rechner".
        foreach (var f in Directory.EnumerateFiles(_modDir)) File.Delete(f);
        Directory.EnumerateFiles(_modDir).Should().BeEmpty();

        // 3) Restore aus dem Backup.
        var restoreResult = await backup.RestoreBackupAsync(backupZip, ct: ct);
        restoreResult.RestoredCount.Should().Be(2);
        restoreResult.SkippedCount.Should().Be(0);

        var filesAfter = Directory.EnumerateFiles(_modDir).Select(Path.GetFileName).ToList();
        filesAfter.Should().Contain("ActiveMod.zip");
        filesAfter.Should().Contain("DisabledMod.zip.disabled");
        filesAfter.Should().HaveCount(2);
    }

    [Fact]
    public void CreateBackup_LeererModOrdner_WirftInvalidOperation()
    {
        if (!OperatingSystem.IsLinux()) return;

        var (backup, _) = BuildServices();

        var act = () => backup.CreateBackupAsync(Path.Combine(_tempRoot, "empty.zip"));
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nichts zu sichern*");
    }

    [Fact]
    public async Task CreateBackup_ManifestEnthaeltAlleMods()
    {
        if (!OperatingSystem.IsLinux()) return;

        CreateModZip(Path.Combine(_modDir, "M1.zip"), title: "Mod 1", version: "1.1", author: "Author A");
        CreateModZip(Path.Combine(_modDir, "M2.zip.disabled"), title: "Mod 2", version: "2.2", author: "Author B");

        var (backup, _) = BuildServices();
        var backupZip = Path.Combine(_tempRoot, "backup.zip");
        await backup.CreateBackupAsync(backupZip, ct: TestContext.Current.CancellationToken);

        var manifest = ModBackupService.ReadManifest(backupZip);
        manifest.Version.Should().Be(ModBackupService.CurrentFormatVersion);
        manifest.Mods.Should().HaveCount(2);
        manifest.Mods.Should().Contain(m => m.FileName == "M1.zip" && m.IsEnabled && m.Author == "Author A");
        manifest.Mods.Should().Contain(m => m.FileName == "M2.zip.disabled" && !m.IsEnabled && m.Author == "Author B");
    }

    [Fact]
    public void ReadManifest_UnbekannteFormatVersion_Wirft()
    {
        if (!OperatingSystem.IsLinux()) return;

        var badBackup = Path.Combine(_tempRoot, "bad.zip");
        // Manifest mit Version 99 (nicht unterstützt) → Restore soll ablehnen,
        // nicht mit Datenverlust weiterlaufen.
        using (var fs = File.Create(badBackup))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "version": 99, "createdUtc": "2026-01-01T00:00:00Z", "appVersion": "0.0.0", "mods": [] }""");
        }

        var act = () => ModBackupService.ReadManifest(badBackup);
        act.Should().Throw<InvalidDataException>().WithMessage("*Format-Version*");
    }

    // -- Test-Helpers ----------------------------------------------------

    private (ModBackupService Backup, ModInstallService Install) BuildServices()
    {
        var settings = new AppSettingsService();
        settings.Update(s => s.ModPathOverride = _modDir);
        var paths = new ModPathService(settings);
        var reader = new ModDescReader();
        var install = new ModInstallService(paths, reader);
        var backup = new ModBackupService(paths, install, reader);
        return (backup, install);
    }

    private static void CreateModZip(string path, string title, string version, string author = "Tester")
    {
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("modDesc.xml");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="86">
                <author>{author}</author>
                <version>{version}</version>
                <title><en>{title}</en></title>
                <description><en>test</en></description>
            </modDesc>
            """);
    }
}
