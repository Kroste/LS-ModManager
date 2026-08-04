using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Localization;
using LSModManager.Services;
using LSModManager.Services.Ai;
using NLog;

namespace LSModManager.ViewModels;

/// <summary>
/// ViewModel für das <see cref="Views.ModDetailWindow"/>. Lädt die Detail-Seite
/// eines Mods vom ModHub und stellt Metadaten + Screenshots dar. Der Download
/// selbst delegiert an das Haupt-ViewModel (persistenter Downloads-Ordner).
///
/// <para><b>KI-Features:</b> optional (nur wenn <see cref="AiSettings.IsEnabled"/>).
/// „Zusammenfassen" kürzt die Beschreibung auf 3-4 Sätze, „Ähnliche Mods" nutzt
/// die Kategorie als Filter und lässt die KI aus einer Kandidatenliste die
/// 5 verwandtesten wählen. Beide Features sind Best-Effort — Fehler landen
/// als StatusText, blockieren aber nicht das restliche Detail-Fenster.</para>
/// </summary>
public sealed partial class ModDetailViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ModHubService _hub;
    private readonly AppSettingsService _settings;
    private readonly AiSettingsService _aiSettings;
    private readonly AiProviderFactory _aiFactory;
    private readonly MainWindowViewModel _main;
    private readonly int _modId;

    public ModDetailViewModel(
        ModHubService hub, AppSettingsService settings,
        AiSettingsService aiSettings, AiProviderFactory aiFactory,
        MainWindowViewModel main, int modId, string initialTitle)
    {
        _hub = hub;
        _settings = settings;
        _aiSettings = aiSettings;
        _aiFactory = aiFactory;
        _main = main;
        _modId = modId;
        Title = initialTitle;
        _description = L.T("ModDetail_Loading");
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _releaseDate = "";
    [ObservableProperty] private string _platform = "";
    [ObservableProperty] private string _rating = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _downloadReady;

    /// <summary>URL der Detail-Seite auf farming-simulator.com (für „Im Browser").</summary>
    public string DetailUrl { get; private set; } = "";

    public ObservableCollection<string> ScreenshotUrls { get; } = new();

    // ---- KI-Sektion ---------------------------------------------------------

    /// <summary>True wenn im SettingsWindow ein KI-Provider ausgewählt ist —
    /// steuert Sichtbarkeit der beiden KI-Buttons. Wird beim Init einmal
    /// gelesen; wenn der User zwischenzeitlich Provider ändert, muss er das
    /// Detail-Fenster neu öffnen.</summary>
    public bool IsAiEnabled => _aiSettings.Current.IsEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SummarizeCommand))]
    private bool _isSummarizing;

    [ObservableProperty] private string _summaryText = "";
    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);
    partial void OnSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasSummary));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindSimilarModsCommand))]
    private bool _isSearchingSimilar;

    /// <summary>Von der KI vorgeschlagene ähnliche Mods (aus dem Katalog zurück-
    /// gemappt). Card-Klick öffnet den Browser (Detail-Fenster-Kette wäre unnötige
    /// Komplexität).</summary>
    public ObservableCollection<ModHubItemViewModel> SimilarMods { get; } = new();
    [ObservableProperty] private bool _similarNoResults;

    /// <summary>True sobald eine Empfehlung gelaufen ist — steuert Sichtbarkeit
    /// der Ergebnis-Card. Bei laufender Suche schon true (dann sieht der User
    /// den „nichts gefunden"-Text nicht, aber die leere Card kurz während der
    /// Suche — akzeptabel, verhindert Flicker beim Erscheinen der Ergebnisse).</summary>
    public bool HasSimilarResults => SimilarMods.Count > 0 || SimilarNoResults;
    partial void OnSimilarNoResultsChanged(bool value) => OnPropertyChanged(nameof(HasSimilarResults));

    // ---- Init und Basis-Commands -------------------------------------------

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var detail = await _hub.FetchModDetailAsync(_modId, lang);
            if (detail is null)
            {
                Description = L.T("ModDetail_LoadFailed");
                return;
            }
            Title = detail.Title;
            Author = detail.Author;
            Category = detail.Category;
            Version = detail.Version;
            SizeText = detail.SizeText;
            ReleaseDate = detail.ReleaseDate;
            Platform = detail.Platform;
            Rating = detail.RatingText;
            Description = detail.DescriptionText;
            ScreenshotUrls.Clear();
            foreach (var url in detail.ScreenshotUrls) ScreenshotUrls.Add(url);
            DetailUrl = detail.DetailUrl;
            DownloadReady = !string.IsNullOrWhiteSpace(detail.DownloadUrl);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Detail-Init fehlgeschlagen für mod_id={id}", _modId);
            Description = L.T("ModDetail_LoadError");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    public async Task DownloadAsync()
    {
        StatusText = L.T("ModDetail_DownloadRunning");
        await _main.DownloadFromDetailAsync(_modId, Title);
        StatusText = L.T("ModDetail_DownloadStarted");
    }

    private bool CanDownload() => DownloadReady;

    /// <summary>
    /// Öffnet die Detail-Seite im System-Browser — Fallback für Fälle, in denen
    /// die App-Ansicht nicht reicht (Kommentare, verlinkte Mods, komplette
    /// Formatierung der Beschreibung).
    /// </summary>
    [RelayCommand]
    public void OpenInBrowser()
    {
        if (string.IsNullOrWhiteSpace(DetailUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(DetailUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Browser nicht öffnen: {url}", DetailUrl);
            StatusText = L.T("ModDetail_BrowserError");
        }
    }

    // ---- Feature 1: Beschreibungs-Zusammenfassung --------------------------

    [RelayCommand(CanExecute = nameof(CanSummarize))]
    public async Task SummarizeAsync()
    {
        var provider = _aiFactory.Create(_aiSettings.Current);
        if (provider is null)
        {
            StatusText = L.T("ModDetail_LoadFailed");
            return;
        }
        try
        {
            IsSummarizing = true;
            StatusText = L.T("ModDetail_AiSummarizing");
            var response = await provider.CompleteAsync(
                AiPromptBuilder.SummarizeSystemPrompt,
                AiPromptBuilder.BuildSummarizeUserPrompt(Title, Description));
            SummaryText = response;
            StatusText = "";
            Log.Info("KI-Zusammenfassung erstellt für {t}: {len} Zeichen", Title, response.Length);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "KI-Zusammenfassung fehlgeschlagen für {t}", Title);
            StatusText = L.F("ModDetail_AiError", ex.Message);
        }
        finally { IsSummarizing = false; }
    }

    private bool CanSummarize() => IsAiEnabled && !IsSummarizing && !string.IsNullOrWhiteSpace(Description);

    // ---- Feature 2: Ähnliche Mods ------------------------------------------

    [RelayCommand(CanExecute = nameof(CanFindSimilar))]
    public async Task FindSimilarModsAsync()
    {
        var provider = _aiFactory.Create(_aiSettings.Current);
        if (provider is null) return;

        try
        {
            IsSearchingSimilar = true;
            SimilarMods.Clear();
            SimilarNoResults = false;
            OnPropertyChanged(nameof(HasSimilarResults)); // ggf. Card wegblenden bei erneuter Suche
            StatusText = L.T("ModDetail_AiSimilarLoading");

            var candidates = _main.GetCatalogCandidatesForSimilar(Category, DetailUrl);
            if (candidates.Count == 0)
            {
                SimilarNoResults = true;
                StatusText = "";
                return;
            }

            var response = await provider.CompleteAsync(
                AiPromptBuilder.SimilarModsSystemPrompt,
                AiPromptBuilder.BuildSimilarModsUserPrompt(
                    Title, Category, Author, candidates.Select(c => c.Title)));

            var titles = AiPromptBuilder.ParseSimilarModTitles(response);
            var matches = _main.FindCatalogEntriesByTitles(titles);
            foreach (var m in matches) SimilarMods.Add(new ModHubItemViewModel(m));

            SimilarNoResults = SimilarMods.Count == 0;
            // Notify explizit — HasSimilarResults liest SimilarMods.Count, das
            // wird durch OnSimilarNoResultsChanged nur getriggert wenn sich der
            // Bool tatsächlich ändert (bei „0 Treffer" bleibt SimilarNoResults
            // auf false wenn's vorher false war → kein Notify).
            OnPropertyChanged(nameof(HasSimilarResults));
            StatusText = "";
            Log.Info("KI-Empfehlung: {n} ähnliche Mods für {t} (aus {c} Kandidaten)",
                SimilarMods.Count, Title, candidates.Count);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "KI-Empfehlung fehlgeschlagen für {t}", Title);
            StatusText = L.F("ModDetail_AiError", ex.Message);
        }
        finally { IsSearchingSimilar = false; }
    }

    private bool CanFindSimilar() => IsAiEnabled && !IsSearchingSimilar && !string.IsNullOrWhiteSpace(Category);

    /// <summary>Klick auf eine „Ähnliche Mods"-Card — öffnet den Browser.
    /// Bewusst kein neues Detail-Fenster (unnötige Fenster-Kette, User kann
    /// die Detail-URL auch selbst im Katalog suchen).</summary>
    [RelayCommand]
    public void OpenSimilarInBrowser(ModHubItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DetailUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.DetailUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Browser nicht öffnen: {url}", item.DetailUrl);
            StatusText = L.T("ModDetail_BrowserError");
        }
    }
}
