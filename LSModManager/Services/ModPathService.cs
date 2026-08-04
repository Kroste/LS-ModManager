using System.IO;
using System.Text.RegularExpressions;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Findet den LS25-Mod-Ordner auf Windows (Documents/My Games) und Linux
/// (Steam-Proton-Präfix in ALLEN Library-Roots aus <c>libraryfolders.vdf</c>).
/// Auto-Erkennung mit manuellem Override via <see cref="Models.AppSettings.ModPathOverride"/>.
/// </summary>
public sealed class ModPathService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string GameFolderName = "FarmingSimulator2025";
    private const string ModsSubdir = "mods";

    // Steam legt auf Linux das Proton-Präfix mit dem alten XP-Namen "My Documents"
    // an — nicht "Documents" wie auf modernen Windows-Versionen.
    private static readonly string[] DocumentsFolderCandidates = { "My Documents", "Documents" };

    private readonly AppSettingsService _settings;

    public ModPathService(AppSettingsService settings) => _settings = settings;

    /// <summary>
    /// Liefert den effektiven Mod-Pfad: erst Override, dann Auto-Detect,
    /// sonst null. Der zurückgegebene Pfad muss NICHT existieren — Install legt
    /// den <c>mods</c>-Unterordner bei Bedarf selbst an.
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

    /// <summary>
    /// Erkennt den Mod-Pfad, indem der ELTERN-Ordner (FS25-Spielordner) existiert.
    /// Der <c>mods</c>-Ordner selbst kann noch fehlen — Steam/Proton legt ihn erst
    /// beim ersten Mod-Install an.
    /// </summary>
    public string? DetectModPath()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            var gameDir = Path.GetDirectoryName(candidate);
            if (gameDir is null || !Directory.Exists(gameDir)) continue;
            Log.Info("Mod-Pfad erkannt: {p} (mods-Unterordner ggf. noch nicht angelegt)", candidate);
            return candidate;
        }
        Log.Info("Kein FS25-Spielordner erkannt — Nutzer muss den Pfad manuell setzen.");
        return null;
    }

    /// <summary>
    /// Kandidatenliste für den Mod-Pfad in Wahrscheinlichkeitsreihenfolge (erster
    /// existenter GameDir gewinnt). Auf Linux werden alle Steam-Library-Roots aus
    /// <c>libraryfolders.vdf</c> plus typische Mount-Points berücksichtigt.
    /// </summary>
    public IEnumerable<string> EnumerateCandidates()
    {
        foreach (var gameDir in EnumerateGameDirectories())
            yield return Path.Combine(gameDir, ModsSubdir);
    }

    private IEnumerable<string> EnumerateGameDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                yield return Path.Combine(docs, "My Games", GameFolderName);
            yield break;
        }

        // Linux: hypothetischer nativer Pfad (falls GIANTS je einen Linux-Port bringt).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".local", "share", GameFolderName);

        // Steam-Library-Roots: bekannte Home-Locations + alles aus libraryfolders.vdf
        // + typische Mount-Points. Deduplizieren via Set.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in EnumerateSteamLibraryRoots(home))
        {
            if (!seen.Add(steamRoot)) continue;
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
                foreach (var docsName in DocumentsFolderCandidates)
                {
                    yield return Path.Combine(
                        appDir, "pfx", "drive_c", "users", "steamuser",
                        docsName, "My Games", GameFolderName);
                }
            }
        }
    }

    /// <summary>
    /// Alle Steam-Library-Roots: Home-Locations, alles aus libraryfolders.vdf und
    /// typische externe Mount-Points (Bazzite/Nobara nutzen /run/media/, Fedora
    /// älter /media/, Ubuntu /media/USERNAME/).
    /// </summary>
    private static IEnumerable<string> EnumerateSteamLibraryRoots(string? home)
    {
        // 1. Bekannte Home-Roots
        var homeRoots = new List<string>();
        if (!string.IsNullOrEmpty(home))
        {
            homeRoots.Add(Path.Combine(home, ".steam", "steam"));
            homeRoots.Add(Path.Combine(home, ".steam", "root"));
            homeRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
            homeRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam",
                "data", "Steam"));
        }
        foreach (var r in homeRoots) yield return r;

        // 2. Aus libraryfolders.vdf: authoritativer Weg — Steam schreibt hier ALLE
        //    Roots rein, inkl. externer Platten.
        foreach (var homeRoot in homeRoots)
        {
            foreach (var extra in ParseLibraryFolders(homeRoot))
                yield return extra;
        }

        // 3. Fallback: typische Mount-Points scannen. Manche Nutzer verschieben
        //    Library-Verzeichnisse per Symlink oder VDF ist noch nicht geschrieben.
        foreach (var extra in ScanMountPointsForSteamLibraries())
            yield return extra;
    }

    /// <summary>
    /// Liest die "path"-Einträge aus <c>steamapps/libraryfolders.vdf</c>. Format ist
    /// key-value mit geschachtelten Blöcken — wir extrahieren nur die relevante
    /// Zeile per Regex (robust gegenüber Whitespace-Varianten).
    /// </summary>
    public static IEnumerable<string> ParseLibraryFolders(string steamRoot)
    {
        var candidates = new[]
        {
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
        };

        foreach (var vdf in candidates)
        {
            if (!File.Exists(vdf)) continue;
            string content;
            try { content = File.ReadAllText(vdf); }
            catch (Exception ex)
            {
                Log.Debug(ex, "libraryfolders.vdf nicht lesbar: {p}", vdf);
                continue;
            }

            foreach (Match m in Regex.Matches(content, @"""path""\s+""([^""]+)""",
                         RegexOptions.IgnoreCase))
            {
                var path = m.Groups[1].Value;
                // Bazzite/Fedora-Atomic-Falle: der VDF-Pfad ist oft /var/home/user/…,
                // das ist ein Symlink von /home/user/…. Beides zurückgeben, damit
                // die Weiterverarbeitung nicht am Symlink hängt.
                yield return path;
                if (path.StartsWith("/var/home/", StringComparison.Ordinal))
                    yield return "/home/" + path.Substring("/var/home/".Length);
            }
        }
    }

    /// <summary>
    /// Scannt typische Mount-Points nach Ordnern, die eine <c>steamapps/</c>-
    /// Struktur enthalten — als Fallback, wenn kein VDF gefunden wurde.
    /// </summary>
    private static IEnumerable<string> ScanMountPointsForSteamLibraries()
    {
        var mountRoots = new[] { "/run/media", "/mnt", "/media" };
        foreach (var mountRoot in mountRoots)
        {
            if (!Directory.Exists(mountRoot)) continue;
            IEnumerable<string> userDirs;
            try { userDirs = Directory.EnumerateDirectories(mountRoot); }
            catch { continue; }

            foreach (var userDir in userDirs)
            {
                IEnumerable<string> driveDirs;
                try { driveDirs = Directory.EnumerateDirectories(userDir); }
                catch { continue; }

                foreach (var driveDir in driveDirs)
                {
                    // Direkter Steam-Root wie /run/media/system/Games/SteamLibrary?
                    if (Directory.Exists(Path.Combine(driveDir, "steamapps")))
                        yield return driveDir;
                    // Oder eine Ebene tiefer (…/SteamLibrary/…/steamapps)?
                    IEnumerable<string> subDirs;
                    try { subDirs = Directory.EnumerateDirectories(driveDir); }
                    catch { continue; }
                    foreach (var sub in subDirs)
                        if (Directory.Exists(Path.Combine(sub, "steamapps")))
                            yield return sub;
                }
            }
        }
    }
}
