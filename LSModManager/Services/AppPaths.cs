using System.IO;

namespace LSModManager.Services;

/// <summary>
/// Zentrale Datei-Pfade für Cache, Downloads und Config. Plattformkonform
/// (%LOCALAPPDATA% / $XDG_CACHE_HOME) und immer über diesen Helper — nicht
/// verstreut in Services hardcoden.
/// </summary>
public static class AppPaths
{
    private const string AppName = "LSModManager";

    /// <summary>Persistenter Downloads-Ordner für heruntergeladene, noch nicht
    /// installierte Mod-ZIPs. Landet unter LocalAppData/Cache (kein Temp!).</summary>
    public static string DownloadsDir
    {
        get
        {
            var dir = Path.Combine(CacheRoot, "downloads");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Persistenter Cache für KI-generierte Mod-Zusammenfassungen.
    /// Eine Datei pro <c>modId</c>, damit dieselbe Zusammenfassung nicht bei
    /// jedem Detail-Öffnen neu Tokens kostet.</summary>
    public static string AiSummariesCacheDir
    {
        get
        {
            var dir = Path.Combine(CacheRoot, "ai-summaries");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Cache für Preview-PNGs aus installierten Mods.</summary>
    public static string PreviewsCacheDir
    {
        get
        {
            var dir = Path.Combine(CacheRoot, "previews");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Basis-Cache-Pfad für die Preview eines Mods (ohne Extension). Der Caller
    /// hängt die richtige Extension (.png / .jpg) je nach Content an.
    /// </summary>
    public static string PreviewCacheBasePathFor(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        return Path.Combine(PreviewsCacheDir, name);
    }

    /// <summary>
    /// Findet eine existierende Preview-Cache-Datei zu einem Mod, egal welche
    /// Bild-Extension sie hat. Nötig, weil Avalonia/Skia auf Linux ein JPG NICHT
    /// laden kann, wenn es .png-Endung hat — wir speichern es also mit der echten
    /// Extension und suchen beim Load beide.
    /// <para>
    /// Priorität: <c>.jpg</c>/<c>.jpeg</c> zuerst (das sind CDN-Cover vom
    /// ModHub-Download — kuratiert, immer aussagekräftig). <c>.png</c> danach
    /// (aus der ZIP extrahiert — bei vielen Mods leerer Platzhalter oder
    /// reines Grayscale-Icon, deshalb schlechter als CDN-Cover).
    /// </para>
    /// </summary>
    public static string? FindExistingPreview(string zipPath)
    {
        var basePath = PreviewCacheBasePathFor(zipPath);
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
        {
            var p = basePath + ext;
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// True, wenn für den Mod bereits ein Katalog-Cover (nicht ZIP-icon.png)
    /// im Cache liegt. Regel:
    /// <list type="bullet">
    /// <item>JPG/JPEG → immer Katalog-Cover (ZIPs liefern nie JPG als icon).</item>
    /// <item>PNG → braucht Sidecar-Marker <c>&lt;basename&gt;.catalog</c>, weil
    /// dieselbe Extension auch von ZIP-icon.png stammen kann (schwarze Platzhalter
    /// überschreiben dann sonst NIE das gute Katalog-Cover).</item>
    /// </list>
    /// Der Backfill nutzt das, um zu entscheiden welche Mods noch kein
    /// kuratiertes Cover haben.
    /// </summary>
    public static bool HasCatalogCoverCache(string zipPath)
    {
        var basePath = PreviewCacheBasePathFor(zipPath);
        if (File.Exists(basePath + ".jpg") || File.Exists(basePath + ".jpeg")) return true;
        // PNG + Marker → aus Katalog. PNG ohne Marker → ZIP-icon (kann ersetzt werden).
        return File.Exists(basePath + ".png") && File.Exists(basePath + ".catalog");
    }

    /// <summary>
    /// Schreibt einen leeren Sidecar-Marker, der eine <c>.png</c>-Preview als
    /// „Katalog-Cover" kennzeichnet — nötig weil PNG-Extension nicht zwischen
    /// ZIP-icon.png und CDN-PNG unterscheiden kann.
    /// </summary>
    public static void WriteCatalogCoverMarker(string zipPath)
    {
        var marker = PreviewCacheBasePathFor(zipPath) + ".catalog";
        try { File.WriteAllBytes(marker, Array.Empty<byte>()); }
        catch { /* best-effort, nicht kritisch */ }
    }

    /// <summary>Extension aus den ersten Bytes der Bild-Daten raten (JPG vs PNG).</summary>
    public static string GuessImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 &&
            bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        return ".bin"; // sollte niemals genutzt werden — signal für „kein Bild"
    }

    public static string CacheRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppName, "cache");
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
                xdg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache");
            return Path.Combine(xdg, AppName);
        }
    }
}
