using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace LSModManager.Localization;

/// <summary>
/// Singleton-Service für UI-Lokalisierung. Liefert übersetzte Strings aus
/// den <c>Strings.*.resx</c>-Ressourcen und benachrichtigt gebundene XAML-
/// Elemente per <see cref="INotifyPropertyChanged"/>, sobald die aktive
/// Sprache wechselt — damit funktioniert der Sprachwechsel live, ohne
/// App-Neustart.
///
/// <b>Community-Sprachpakete:</b> Um eine weitere Sprache hinzuzufügen,
/// braucht es zwei Änderungen (PR gegen das Repo):
/// <list type="number">
///   <item>Neue <c>Strings.&lt;iso&gt;.resx</c> in diesem Ordner mit den
///   übersetzten Keys (Deutsch/Englisch als Vorlage nutzen).</item>
///   <item>Zeile in <see cref="SupportedCultures"/> ergänzen mit ISO-Code,
///   nativem Sprachnamen und Länderflaggen-Emoji.</item>
/// </list>
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    /// <summary>
    /// Vom App-Nutzer konfigurierbare Sprachen. ISO-Code passt zur
    /// <c>Strings.{iso}.resx</c>-Dateinamenskonvention. Neutral (Fallback)
    /// ist Englisch — die englische <c>Strings.resx</c> liegt ohne Suffix.
    /// Englisch bekommt die UK-Flagge (🇬🇧) als international neutrales
    /// Symbol. Weitere Sprachen werden hier ergänzt.
    /// </summary>
    public static IReadOnlyList<(string Iso, string Display, string Flag)> SupportedCultures { get; } = new[]
    {
        ("en", "English", "🇬🇧"),
        ("de", "Deutsch", "🇩🇪"),
        // Community-PRs: hier weitere Sprachen ergänzen und
        // Strings.<iso>.resx daneben legen.
        // ("fr", "Français", "🇫🇷"),
        // ("es", "Español", "🇪🇸"),
        // ("it", "Italiano", "🇮🇹"),
        // ("pl", "Polski", "🇵🇱"),
        // ("nl", "Nederlands", "🇳🇱"),
    };

    private readonly ResourceManager _rm = new(
        "LSModManager.Localization.Strings",
        typeof(LocalizationService).Assembly);

    private CultureInfo _current = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Aktuell aktive UI-Sprache. Bei Zuweisung wird
    /// <see cref="LocalizedString.NotifyAllChanged"/> aufgerufen — feuert auf
    /// jedem lebenden Wrapper ein reguläres <c>PropertyChanged(nameof(Value))</c>,
    /// was von Avalonias Binding-Engine zuverlässig aufgelöst wird.
    /// <para>
    /// <b>Wichtig:</b> nicht auf <c>PropertyChanged("Item[]")</c> setzen —
    /// diese WPF-Indexer-Konvention wird von Avalonia 12 nur unzuverlässig
    /// gehandhabt (Bindings in nicht-fokussierten Fenstern bleiben stale
    /// bis zum nächsten Fenster-Aufbau — siehe pitfalls.md).
    /// </para>
    /// </summary>
    public CultureInfo Current
    {
        get => _current;
        set
        {
            if (Equals(_current, value)) return;
            _current = value;
            CultureInfo.CurrentUICulture = value;
            LocalizedString.NotifyAllChanged();
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(CurrentIso));
        }
    }

    public string CurrentIso => TwoLetterOrDefault(_current);

    /// <summary>
    /// Setzt die Sprache anhand ihres ISO-Codes ("en"/"de"/...). Unbekannte
    /// Codes werden auf die Neutral-Kultur (EN) gemappt.
    /// </summary>
    public void SetCulture(string iso)
    {
        Current = SupportedCultures.Any(c => c.Iso == iso)
            ? CultureInfo.GetCultureInfo(iso)
            : CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Indexer für XAML-Binding. Fallback bei fehlendem Key: der Key selbst
    /// als <c>!Key!</c> — dann fällt eine unlokalisierte Stelle sofort auf.
    /// </summary>
    public string this[string key]
    {
        get
        {
            try
            {
                return _rm.GetString(key, _current) ?? $"!{key}!";
            }
            catch (MissingManifestResourceException)
            {
                return $"!{key}!";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string TwoLetterOrDefault(CultureInfo c)
    {
        var iso = c.TwoLetterISOLanguageName;
        return SupportedCultures.Any(x => x.Iso == iso) ? iso : "en";
    }
}
