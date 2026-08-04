using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Localization;
using LSModManager.Services;
using NLog;

namespace LSModManager.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Wenn GIANTS eine Sprache hinzufügt, hier ergänzen — die Liste läuft direkt
    // in die ComboBox des Settings-Fensters.
    public static readonly string[] SupportedLanguagesList = { "de", "en", "fr", "es", "it", "pl" };

    private readonly AppSettingsService _settings;
    private readonly ModPathService _paths;

    public SettingsWindowViewModel(AppSettingsService settings, ModPathService paths)
    {
        _settings = settings;
        _paths = paths;

        ModPathOverride = _settings.Current.ModPathOverride ?? "";
        DetectedPath = _paths.DetectModPath() ?? "(nicht gefunden)";
        var stored = _settings.Current.CatalogLanguage;
        CatalogLanguage = SupportedLanguagesList.Contains(stored, StringComparer.OrdinalIgnoreCase)
            ? stored!
            : "de";
        CatalogRefreshHours = _settings.Current.CatalogRefreshHours;

        // UI-Sprache: aus Settings oder aktuelle Kultur, gemappt auf einen
        // Supported-Cultures-Eintrag. Fallback auf Englisch.
        var storedUi = _settings.Current.UiCulture ?? LocalizationService.Instance.CurrentIso;
        SelectedUiCulture = UiCultures.FirstOrDefault(c => c.Iso == storedUi)
                            ?? UiCultures.First(c => c.Iso == "en");
    }

    /// <summary>Verfügbare UI-Sprachen als DTO für die ComboBox (Flag + Name).</summary>
    public IReadOnlyList<UiCultureOption> UiCultures { get; } =
        LocalizationService.SupportedCultures
            .Select(c => new UiCultureOption(c.Iso, c.Display, c.Flag))
            .ToList();

    [ObservableProperty] private UiCultureOption? _selectedUiCulture;

    partial void OnSelectedUiCultureChanged(UiCultureOption? value)
    {
        // Live-Umschalten — der Nutzer sieht das Ergebnis sofort im offenen
        // Fenster (und in allen anderen dank statisch gecachtem LocalizedString).
        if (value is not null) LocalizationService.Instance.SetCulture(value.Iso);
    }

    public IReadOnlyList<string> SupportedLanguages => SupportedLanguagesList;

    /// <summary>Cache-Auto-Refresh-Optionen (Stunden). 0 = immer neu laden.</summary>
    public IReadOnlyList<CacheRefreshOption> RefreshOptions { get; } = new[]
    {
        new CacheRefreshOption(0, "Bei jedem Start neu laden"),
        new CacheRefreshOption(1, "Nach 1 Stunde"),
        new CacheRefreshOption(6, "Nach 6 Stunden"),
        new CacheRefreshOption(12, "Nach 12 Stunden"),
        new CacheRefreshOption(24, "Nach 24 Stunden (empfohlen)"),
        new CacheRefreshOption(24 * 7, "Nach 7 Tagen"),
        new CacheRefreshOption(int.MaxValue, "Nie automatisch"),
    };

    [ObservableProperty] private int _catalogRefreshHours = 24;

    public CacheRefreshOption? SelectedRefreshOption
    {
        get => RefreshOptions.FirstOrDefault(o => o.Hours == CatalogRefreshHours)
               ?? RefreshOptions.First(o => o.Hours == 24);
        set { if (value is not null) CatalogRefreshHours = value.Hours; }
    }
    partial void OnCatalogRefreshHoursChanged(int value) => OnPropertyChanged(nameof(SelectedRefreshOption));

    public event EventHandler? SettingsChanged;

    [ObservableProperty] private string _modPathOverride = "";
    [ObservableProperty] private string _detectedPath = "";
    [ObservableProperty] private string _catalogLanguage = "de";

    [RelayCommand]
    public void Detect()
    {
        DetectedPath = _paths.DetectModPath() ?? "(nicht gefunden)";
        Log.Info("Manuelle Pfad-Detection: {p}", DetectedPath);
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _settings.Update(s =>
        {
            s.ModPathOverride = string.IsNullOrWhiteSpace(ModPathOverride) ? null : ModPathOverride.Trim();
            s.CatalogLanguage = CatalogLanguage;
            s.CatalogRefreshHours = CatalogRefreshHours;
            s.UiCulture = SelectedUiCulture?.Iso;
        });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        Log.Info("Settings gespeichert: override={o} lang={l}",
            _settings.Current.ModPathOverride ?? "<null>", _settings.Current.CatalogLanguage);
    }

    public void ApplyPickedPath(string path)
    {
        ModPathOverride = path;
    }
}

/// <summary>Auswahl-Option für den Katalog-Cache-Refresh-Intervall.</summary>
public sealed record CacheRefreshOption(int Hours, string Label);

/// <summary>Auswahl-Option für die UI-Sprache im Settings-Fenster.</summary>
public sealed record UiCultureOption(string Iso, string Display, string Flag)
{
    /// <summary>Für die ComboBox-Anzeige: „🇬🇧 English" / „🇩🇪 Deutsch".</summary>
    public string DisplayWithFlag => $"{Flag}  {Display}";
}
