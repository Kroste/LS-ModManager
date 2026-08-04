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
            Log.Info("Katalog-Cache geladen: {n} Einträge (Alter: {age})",
                snapshot.Entries.Count, DateTime.UtcNow - snapshot.SavedUtc);
            return snapshot;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-Cache defekt — ignoriere: {p}", path);
            return null;
        }
    }
}

public sealed record CatalogSnapshot(
    DateTime SavedUtc,
    string Language,
    List<ModHubEntry> Entries);
