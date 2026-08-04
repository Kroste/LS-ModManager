namespace LSModManager.Models;

/// <summary>
/// Ein Eintrag aus einem Mod-Katalog (GIANTS ModHub oder Modhoster).
/// <para>
/// <see cref="Source"/> unterscheidet die Anbieter, <see cref="CanInAppDownload"/>
/// signalisiert dem UI, ob ein Direct-Download möglich ist (nur GIANTS) oder ob
/// die Detail-URL im Browser geöffnet werden muss (Modhoster: Login-Pflicht,
/// robots.txt verbietet Auto-Download).
/// </para>
/// </summary>
public sealed record ModHubEntry(
    string Title,
    string Author,
    string Category,
    string PreviewUrl,
    string DetailUrl,
    string? Version,
    string? SizeText,
    string Source = ModHubEntry.GiantsSource,
    bool CanInAppDownload = true)
{
    public const string GiantsSource = "GiantsModHub";
    public const string ModhosterSource = "Modhoster";
    public const string HofHirschfeldSource = "Hof Hirschfeld";
}
