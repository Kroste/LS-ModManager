using System.IO;
using System.Text.Json;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Persistenter Katalog-Cache. GIANTS liefert keinen search-Parameter, wir müssen
/// clientseitig alle Seiten sammeln — das dauert ~15 Sekunden. Dieser Cache
/// überlebt App-Neustarts und macht den Katalog beim nächsten Start sofort
/// benutzbar. Refresh-Button lädt trotzdem alles neu.
/// </summary>
public static class CatalogCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static string CachePath(string language) =>
        Path.Combine(AppPaths.CacheRoot, $"catalog-{language}.json");

    /// <summary>Sidecar-Datei mit den DetailUrls, die der Nutzer beim vorherigen
    /// App-Start schon im Katalog hatte. Diff gegen den aktuellen Katalog =
    /// „neue Mods seit letztem Start". Nur URLs, eine pro Zeile — kein JSON,
    /// spart Platz (~900 KB → ~500 KB) und ist einfacher zu diffen.</summary>
    private static string SeenPath(string language) =>
        Path.Combine(AppPaths.CacheRoot, $"catalog-{language}-seen.txt");

    /// <summary>
    /// Lädt die beim vorherigen App-Start bekannten DetailUrls, gegen die
    /// „neue" Einträge geprüft werden. Gibt <c>null</c> zurück wenn keine
    /// Sidecar-Datei existiert (Erst-Start oder frisch gelöschter Cache) —
    /// der Caller darf dann NICHTS als neu markieren, sonst leuchtet beim
    /// ersten Start alles auf.
    /// </summary>
    public static HashSet<string>? LoadSeenSnapshot(string language)
    {
        var path = SeenPath(language);
        if (!File.Exists(path)) return null;
        try
        {
            return new HashSet<string>(File.ReadAllLines(path), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Seen-Snapshot defekt — ignoriere: {p}", path);
            return null;
        }
    }

    /// <summary>Schreibt die aktuellen DetailUrls als neuen „beim letzten Start
    /// bekannt"-Snapshot. Wird nach dem initialen Katalog-Load und nach dem
    /// Ende eines Full-Loads aufgerufen — beim nächsten Start ist damit
    /// „alles was der User jetzt sieht" der Vergleichs-Baseline.</summary>
    public static void SaveSeenSnapshot(IEnumerable<string> detailUrls, string language)
    {
        var path = SeenPath(language);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, detailUrls);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { Log.Warn(ex, "Seen-Snapshot konnte nicht geschrieben werden: {p}", path); }
    }

    public static void Save(IEnumerable<ModHubEntry> entries, string language)
    {
        var path = CachePath(language);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new CatalogSnapshot(DateTime.UtcNow, language, entries.ToList());
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, JsonOpts));
        File.Move(tmp, path, overwrite: true);
        Log.Info("Katalog-Cache geschrieben: {n} Einträge → {p}", payload.Entries.Count, path);
    }

    public static CatalogSnapshot? Load(string language)
    {
        var path = CachePath(language);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(json);
            if (snapshot is null) return null;

            // Historische Fallback-Einträge aus alten Cache-Dateien filtern
            // (Titel „Mod {id}" ohne Autor/Preview — der Parser vor v0.4.2 hat
            // die noch reingelegt, jetzt werden sie beim Parsen geskippt).
            // Beim nächsten Full-Load-Save schreibt sich der Cache sauber neu.
            var cleaned = snapshot.Entries
                .Where(e => !FallbackTitlePattern.IsMatch(e.Title ?? ""))
                .ToList();
            var removed = snapshot.Entries.Count - cleaned.Count;
            if (removed > 0)
                Log.Info("Katalog-Cache: {r} Alt-Fallback-Einträge beim Load gefiltert", removed);

            Log.Info("Katalog-Cache geladen: {n} Einträge (Alter: {age})",
                cleaned.Count, DateTime.UtcNow - snapshot.SavedUtc);
            return snapshot with { Entries = cleaned };
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-Cache defekt — ignoriere: {p}", path);
            return null;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex FallbackTitlePattern =
        new(@"^Mod \d+$", System.Text.RegularExpressions.RegexOptions.Compiled);
}

public sealed record CatalogSnapshot(
    DateTime SavedUtc,
    string Language,
    List<ModHubEntry> Entries);
