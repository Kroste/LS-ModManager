using System.IO;
using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

/// <summary>
/// Verifiziert die Kroste-Persistenz-Regel: defekte Config-Datei wird als
/// <c>.broken</c> gesichert, nicht kommentarlos überschrieben.
/// </summary>
[Collection("EnvironmentIsolation")]
public sealed class AppSettingsBrokenBackupTests : IDisposable
{
    private readonly string _tempConfigDir;
    private readonly string _tempConfigPath;
    private readonly string _brokenPath;
    private readonly string? _originalXdg;

    public AppSettingsBrokenBackupTests()
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), "lsmm-cfg-" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(_tempConfigDir, "LSModManager");
        Directory.CreateDirectory(appDir);
        _tempConfigPath = Path.Combine(appDir, "settings.json");
        _brokenPath = _tempConfigPath + ".broken";
        // AppSettingsService liest XDG_CONFIG_HOME auf Linux — auf Windows geht's
        // über SpecialFolder.ApplicationData. Test läuft unter Linux (CI/Bazzite).
        _originalXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdg);
        try { Directory.Delete(_tempConfigDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_DefekteJson_WirdAlsBrokenGesichert()
    {
        // Skip auf Windows: dort geht der Pfad über SpecialFolder, XDG hat keinen Effekt.
        if (!OperatingSystem.IsLinux()) return;

        File.WriteAllText(_tempConfigPath, "{ das_ist_kein_json ]");

        var svc = new AppSettingsService();

        // Original ist als .broken gesichert
        File.Exists(_brokenPath).Should().BeTrue();
        File.ReadAllText(_brokenPath).Should().Contain("das_ist_kein_json");
        // Original-Pfad ist leer (bis zum ersten Save)
        File.Exists(_tempConfigPath).Should().BeFalse();
        // Defaults sind aktiv
        svc.Current.CatalogLanguage.Should().Be("de");
        svc.Current.CatalogRefreshHours.Should().Be(24);
    }

    [Fact]
    public void Load_ValideJson_KeinBrokenBackup()
    {
        if (!OperatingSystem.IsLinux()) return;

        File.WriteAllText(_tempConfigPath,
            """{"CatalogLanguage":"en","CatalogRefreshHours":6}""");

        var svc = new AppSettingsService();

        File.Exists(_brokenPath).Should().BeFalse();
        svc.Current.CatalogLanguage.Should().Be("en");
        svc.Current.CatalogRefreshHours.Should().Be(6);
    }
}
