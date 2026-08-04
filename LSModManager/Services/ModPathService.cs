using System.IO;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Findet den LS25-Mod-Ordner auf Windows (Documents/My Games) und Linux
/// (Steam-Proton-Präfix, alle bekannten Steam-Library-Roots). Auto-Erkennung mit
/// manuellem Override via <see cref="Models.AppSettings.ModPathOverride"/>.
/// </summary>
public sealed class ModPathService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string GameFolderName = "FarmingSimulator2025";
    private const string ModsSubdir = "mods";

    private readonly AppSettingsService _settings;

    public ModPathService(AppSettingsService settings) => _settings = settings;

    /// <summary>
    /// Liefert den effektiven Mod-Pfad: erst Override, dann Auto-Detect,
    /// sonst null. Wird nicht angelegt — Installation prüft und legt bei Bedarf an.
    /// </summary>
    public string? GetModPath()
    {
        var manual = _settings.Current.ModPathOverride;
        if (!string.IsNullOrWhiteSpace(manual))
        {
            Log.Debug("Nutze manuellen Mod-Pfad: {p}", manual);
            return manual;
        }
        return DetectModPath();
    }

    public string? DetectModPath()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (Directory.Exists(candidate))
            {
                Log.Info("Mod-Pfad erkannt: {p}", candidate);
                return candidate;
            }
        }
        Log.Info("Kein Mod-Pfad erkannt — Nutzer muss ihn manuell setzen.");
        return null;
    }

    /// <summary>Kandidatenliste in Wahrscheinlichkeitsreihenfolge (erster Treffer gewinnt).</summary>
    public IEnumerable<string> EnumerateCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                yield return Path.Combine(docs, "My Games", GameFolderName, ModsSubdir);
            yield break;
        }

        // Linux: hypothetischer nativer Pfad (falls GIANTS je einen Linux-Port bringt)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".local", "share", GameFolderName, ModsSubdir);

            // Steam-Proton-Präfixe: sämtliche typischen Library-Roots scannen.
            foreach (var steamRoot in EnumerateSteamRoots(home))
            {
                var compatData = Path.Combine(steamRoot, "steamapps", "compatdata");
                if (!Directory.Exists(compatData)) continue;

                IEnumerable<string> appDirs;
                try { appDirs = Directory.EnumerateDirectories(compatData); }
                catch (Exception ex)
                {
                    Log.Debug(ex, "compatdata nicht lesbar: {p}", compatData);
                    continue;
                }
                foreach (var appDir in appDirs)
                {
                    var candidate = Path.Combine(
                        appDir, "pfx", "drive_c", "users", "steamuser",
                        "Documents", "My Games", GameFolderName, ModsSubdir);
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots(string home)
    {
        // Klassisches Steam
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".local", "share", "Steam");
        // Flatpak-Steam
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam",
            "data", "Steam");
    }
}
