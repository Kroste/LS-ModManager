using System.IO;
using System.Text.Json;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Lädt und speichert <see cref="AppSettings"/> unter dem plattformkonformen
/// Konfigurationspfad (%APPDATA% / $XDG_CONFIG_HOME). Atomar via tmp+move.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _configPath;
    private AppSettings _current;

    public AppSettingsService()
    {
        var dir = GetConfigDir();
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_current);
        Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_current, JsonOpts);
            var tmp = _configPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _configPath, overwrite: true);
            Log.Debug("Settings gespeichert: {p}", _configPath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Settings nicht speichern: {p}", _configPath);
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_configPath))
        {
            Log.Info("Keine Settings-Datei — nutze Defaults");
            return new AppSettings();
        }
        try
        {
            var json = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            return loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Kroste-Persistenz-Regel: defekte Datei NIE kommentarlos überschreiben.
            // Als .broken sichern — Diagnose bleibt möglich, Nutzer verliert die
            // Original-Werte nicht endgültig, App startet mit Defaults weiter.
            var brokenPath = _configPath + ".broken";
            try
            {
                if (File.Exists(brokenPath)) File.Delete(brokenPath);
                File.Move(_configPath, brokenPath);
                Log.Error(ex, "Settings-Datei defekt — als .broken gesichert: {p}", brokenPath);
            }
            catch (Exception moveEx)
            {
                Log.Warn(moveEx, "Konnte defekte Settings nicht als .broken sichern");
            }
            return new AppSettings();
        }
    }

    private static string GetConfigDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "LSModManager");
        }
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(xdg))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdg = Path.Combine(home, ".config");
        }
        return Path.Combine(xdg, "LSModManager");
    }
}
