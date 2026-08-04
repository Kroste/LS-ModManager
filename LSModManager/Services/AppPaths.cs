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
