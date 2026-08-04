namespace LSModManager.Models;

/// <summary>
/// Persistente Nutzer-Einstellungen. Werden als JSON unter
/// %APPDATA%/LSModManager (Windows) bzw. $XDG_CONFIG_HOME/LSModManager (Linux)
/// abgelegt. Keine Secrets — API-Keys gehören ins Kroste-SecretProtection.
/// </summary>
public sealed class AppSettings
{
    private string _catalogLanguage = "de";

    /// <summary>Manueller Override für den LS25-Mod-Pfad. Wenn null: Auto-Detect.</summary>
    public string? ModPathOverride { get; set; }

    /// <summary>Zeitpunkt des letzten erfolgreichen Katalog-Fetches.</summary>
    public DateTime? LastCatalogFetchUtc { get; set; }

    /// <summary>
    /// Nach wie vielen Stunden gilt der Cache als abgelaufen und wird beim
    /// App-Start automatisch neu geladen. 0 = immer neu laden. Default 24h.
    /// </summary>
    public int CatalogRefreshHours { get; set; } = 24;

    /// <summary>
    /// Steam-AppID des Spiels für den <c>steam://run/</c>-Launcher. FS25 = 2300320.
    /// Kann überschrieben werden, falls GIANTS die ID ändert oder für FS22-Fork.
    /// </summary>
    public int SteamAppId { get; set; } = 2300320;

    /// <summary>
    /// UI-Sprache (ISO-Code, z.B. "en"/"de"). Wird beim App-Start vor dem
    /// MainWindow-Bau in <see cref="Localization.LocalizationService"/>
    /// gesetzt. Null → Betriebssystem-Kultur.
    /// </summary>
    public string? UiCulture { get; set; }

    /// <summary>
    /// Sprachfilter für den Katalog: <c>de/en/fr/es/it/pl</c>. Setter sanitisiert
    /// gegen Müll (z.B. falsche ComboBox-Bindings, die früher das ComboBoxItem-
    /// Objekt statt den String persistiert haben).
    /// </summary>
    public string CatalogLanguage
    {
        get => _catalogLanguage;
        set => _catalogLanguage = Sanitize(value);
    }

    private static readonly HashSet<string> AllowedLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "de", "en", "fr", "es", "it", "pl" };

    private static string Sanitize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return "de";
        var trimmed = candidate.Trim();
        return AllowedLanguages.Contains(trimmed) ? trimmed.ToLowerInvariant() : "de";
    }
}
