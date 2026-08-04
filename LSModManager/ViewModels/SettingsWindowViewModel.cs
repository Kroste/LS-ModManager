using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Localization;
using LSModManager.Services;
using LSModManager.Services.Ai;
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
    private readonly AiSettingsService _aiSettings;
    private readonly AiProviderFactory _aiFactory;

    public SettingsWindowViewModel(
        AppSettingsService settings, ModPathService paths,
        AiSettingsService aiSettings, AiProviderFactory aiFactory)
    {
        _settings = settings;
        _paths = paths;
        _aiSettings = aiSettings;
        _aiFactory = aiFactory;

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

        // KI-Sektion: aktueller Provider + Felder des aktiven Providers.
        // Die Configs der nicht-aktiven Provider bleiben unangetastet in
        // _aiSettings.Current und werden beim Save mit erhalten.
        _aiProvider = _aiSettings.Current.Provider;
        LoadActiveProviderFields();
    }

    private void LoadActiveProviderFields()
    {
        var cfg = ProviderConfig(_aiSettings.Current, AiProvider);
        AiEndpoint = cfg.Endpoint;
        AiModel = cfg.Model;
        AiApiKey = cfg.ApiKey ?? "";
        // Diese sind computed properties (Ableitung aus AiProvider), nicht
        // ObservableProperty — muss ich manuell notifien.
        OnPropertyChanged(nameof(IsAiConfigured));
        OnPropertyChanged(nameof(NeedsApiKey));
        OnPropertyChanged(nameof(IsOllamaProvider));
    }

    private static AiProviderConfig ProviderConfig(AiSettings s, AiProviderType p) => p switch
    {
        AiProviderType.Ollama => s.Ollama,
        AiProviderType.Anthropic => s.Anthropic,
        AiProviderType.OpenAi => s.OpenAi,
        AiProviderType.Gemini => s.Gemini,
        AiProviderType.Mistral => s.Mistral,
        AiProviderType.OpenAiCompatible => s.OpenAiCompatible,
        _ => AiDefaults.Config(AiProviderType.Ollama),
    };

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

    // ---- KI-Sektion ----------------------------------------------------------

    /// <summary>Alle Provider-Enum-Werte für die Auswahl-ComboBox. Reihenfolge
    /// entspricht dem enum — „None" oben, dann Ollama (Default-Empfehlung),
    /// dann die Cloud-Anbieter.</summary>
    public IReadOnlyList<AiProviderType> AiProviders { get; } =
        Enum.GetValues<AiProviderType>();

    /// <summary>Kuratierte Ollama-Modell-Empfehlungen für die Download-Section.</summary>
    public IReadOnlyList<OllamaCuratedModel> RecommendedOllamaModels { get; } =
        OllamaCuratedModels.All;

    [ObservableProperty] private AiProviderType _aiProvider = AiProviderType.None;

    partial void OnAiProviderChanged(AiProviderType value)
    {
        LoadActiveProviderFields();
    }

    [ObservableProperty] private string _aiEndpoint = "";
    [ObservableProperty] private string _aiModel = "";
    [ObservableProperty] private string _aiApiKey = "";

    /// <summary>Nicht-None-Auswahl heißt: die App wird KI-Features aktivieren.
    /// Steuert das UI-Enable der Config-Felder.</summary>
    public bool IsAiConfigured => AiProvider != AiProviderType.None;

    /// <summary>Cloud-Provider brauchen einen API-Key; Ollama und None nicht.</summary>
    public bool NeedsApiKey => AiProvider is AiProviderType.Anthropic
        or AiProviderType.OpenAi or AiProviderType.Gemini or AiProviderType.Mistral;

    public bool IsOllamaProvider => AiProvider == AiProviderType.Ollama;

    [ObservableProperty] private OllamaCuratedModel? _selectedRecommendedOllamaModel;

    [ObservableProperty] private string _aiTestResult = "";

    /// <summary>Fragt <see cref="IAiProvider.IsAvailableAsync"/> mit den GERADE
    /// eingegebenen (noch nicht gespeicherten) Werten. Der Nutzer sieht sofort
    /// ob Endpoint/Key stimmen, ohne Save-Zwischenschritt.</summary>
    [RelayCommand]
    public async Task TestAiConnectionAsync()
    {
        AiTestResult = "Prüfe …";
        try
        {
            // Temporäre Settings mit den aktuellen UI-Werten bauen.
            var testSettings = BuildAiSettingsFromUi();
            var provider = _aiFactory.Create(testSettings);
            if (provider is null)
            {
                AiTestResult = AiProvider == AiProviderType.None
                    ? "KI ist deaktiviert."
                    : "Provider konnte nicht erstellt werden (API-Key fehlt?).";
                return;
            }
            var ok = await provider.IsAvailableAsync();
            AiTestResult = ok
                ? $"✓ Verbindung zu {provider.Name} erfolgreich."
                : $"✗ {provider.Name}: kein Zugriff (Endpoint erreichbar?).";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "KI-Verbindungstest fehlgeschlagen");
            AiTestResult = "✗ Fehler: " + ex.Message;
        }
    }

    /// <summary>Öffnet das Ollama-Pull-Fenster für das gewählte Empfehlungsmodell.
    /// Nutzt die UI-Werte (nicht die persistierten) für den Endpoint, damit ein
    /// noch nicht gespeicherter Endpoint-Wechsel schon getestet werden kann.
    /// Der eigentliche Fenster-Aufruf läuft im Code-Behind (View-Concern) —
    /// hier wird nur das Event gefeuert.</summary>
    [RelayCommand(CanExecute = nameof(CanPullOllama))]
    public void PullRecommendedOllamaModel()
    {
        if (SelectedRecommendedOllamaModel is null) return;
        var testSettings = BuildAiSettingsFromUi();
        var provider = _aiFactory.CreateOllama(testSettings);
        OllamaPullRequested?.Invoke(provider, SelectedRecommendedOllamaModel.Name);
    }
    private bool CanPullOllama() => SelectedRecommendedOllamaModel is not null;
    partial void OnSelectedRecommendedOllamaModelChanged(OllamaCuratedModel? value)
        => PullRecommendedOllamaModelCommand.NotifyCanExecuteChanged();

    /// <summary>Signal an das SettingsWindow: bitte einen Ollama-Pull mit dem
    /// gegebenen Provider und Modellnamen starten. View-Concern — VMs erzeugen
    /// keine Views.</summary>
    public event Action<OllamaProvider, string>? OllamaPullRequested;

    private AiSettings BuildAiSettingsFromUi()
    {
        var current = _aiSettings.Current;
        var updated = new AiProviderConfig(
            Endpoint: string.IsNullOrWhiteSpace(AiEndpoint) ? AiDefaults.Endpoint(AiProvider) : AiEndpoint.Trim(),
            Model: string.IsNullOrWhiteSpace(AiModel) ? AiDefaults.Model(AiProvider) : AiModel.Trim(),
            ApiKey: string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey.Trim());
        return AiProvider switch
        {
            AiProviderType.Ollama            => current with { Provider = AiProvider, Ollama = updated },
            AiProviderType.Anthropic         => current with { Provider = AiProvider, Anthropic = updated },
            AiProviderType.OpenAi            => current with { Provider = AiProvider, OpenAi = updated },
            AiProviderType.Gemini            => current with { Provider = AiProvider, Gemini = updated },
            AiProviderType.Mistral           => current with { Provider = AiProvider, Mistral = updated },
            AiProviderType.OpenAiCompatible  => current with { Provider = AiProvider, OpenAiCompatible = updated },
            _                                => current with { Provider = AiProviderType.None },
        };
    }

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
        // KI-Settings separat persistiert (eigene Datei ai-settings.json).
        _aiSettings.Update(BuildAiSettingsFromUi());
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        Log.Info("Settings gespeichert: override={o} lang={l} ai={ai}",
            _settings.Current.ModPathOverride ?? "<null>",
            _settings.Current.CatalogLanguage,
            _aiSettings.Current.Provider);
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
