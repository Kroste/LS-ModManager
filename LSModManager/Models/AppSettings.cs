namespace LSModManager.Models;

/// <summary>
/// Persistente Nutzer-Einstellungen. Werden als JSON unter
/// %APPDATA%/LSModManager (Windows) bzw. $XDG_CONFIG_HOME/LSModManager (Linux)
/// abgelegt. Keine Secrets — API-Keys gehören ins Kroste-SecretProtection.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Manueller Override für den LS25-Mod-Pfad. Wenn null: Auto-Detect.</summary>
    public string? ModPathOverride { get; set; }

    /// <summary>Zeitpunkt des letzten erfolgreichen Katalog-Fetches.</summary>
    public DateTime? LastCatalogFetchUtc { get; set; }

    /// <summary>Sprachfilter für den Katalog: "de", "en" — Default "de".</summary>
    public string CatalogLanguage { get; set; } = "de";
}
