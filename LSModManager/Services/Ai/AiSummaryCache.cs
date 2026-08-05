using System.IO;
using NLog;

namespace LSModManager.Services.Ai;

/// <summary>
/// Persistenter Cache für KI-generierte Mod-Zusammenfassungen. Eine schlichte
/// Textdatei pro <c>modId</c> unter <see cref="AppPaths.AiSummariesCacheDir"/>,
/// keine Metadaten drum herum — der User klickt „Zusammenfassen" nochmal wenn
/// er einen frischen Anlauf will (überschreibt die Datei).
///
/// <para>Bewusst KEIN Provider/Modell-Suffix im Dateinamen: bei einem Provider-
/// Wechsel wäre der Cache-Eintrag technisch stale, aber die neue Zusammenfassung
/// wäre auch nicht dramatisch anders (dieselbe Beschreibung, ähnliches Ergebnis).
/// Ein Suffix würde nur Cache-Wildwuchs erzeugen und den User verwirren.</para>
/// </summary>
public static class AiSummaryCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static string PathFor(int modId) =>
        Path.Combine(AppPaths.AiSummariesCacheDir, $"{modId}.txt");

    public static string? Read(int modId)
    {
        try
        {
            var path = PathFor(modId);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AI-Summary-Cache-Read fehlgeschlagen für mod_id={id}", modId);
            return null;
        }
    }

    public static void Write(int modId, string summary)
    {
        try
        {
            File.WriteAllText(PathFor(modId), summary);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "AI-Summary-Cache-Write fehlgeschlagen für mod_id={id}", modId);
        }
    }
}
