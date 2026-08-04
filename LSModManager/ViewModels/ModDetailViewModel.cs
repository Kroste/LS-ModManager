using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Services;
using NLog;

namespace LSModManager.ViewModels;

/// <summary>
/// ViewModel für das <see cref="Views.ModDetailWindow"/>. Lädt die Detail-Seite
/// eines Mods vom ModHub und stellt Metadaten + Screenshots dar. Der Download
/// selbst delegiert an das Haupt-ViewModel (persistenter Downloads-Ordner).
/// </summary>
public sealed partial class ModDetailViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ModHubService _hub;
    private readonly AppSettingsService _settings;
    private readonly MainWindowViewModel _main;
    private readonly int _modId;

    public ModDetailViewModel(ModHubService hub, AppSettingsService settings,
        MainWindowViewModel main, int modId, string initialTitle)
    {
        _hub = hub;
        _settings = settings;
        _main = main;
        _modId = modId;
        Title = initialTitle;
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _releaseDate = "";
    [ObservableProperty] private string _platform = "";
    [ObservableProperty] private string _rating = "";
    [ObservableProperty] private string _description = "Lade Details …";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _downloadReady;

    /// <summary>URL der Detail-Seite auf farming-simulator.com (für „Im Browser").</summary>
    public string DetailUrl { get; private set; } = "";

    public ObservableCollection<string> ScreenshotUrls { get; } = new();

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var detail = await _hub.FetchModDetailAsync(_modId, lang);
            if (detail is null)
            {
                Description = "Details konnten nicht geladen werden.";
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
            Description = "Fehler beim Laden.";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    public async Task DownloadAsync()
    {
        StatusText = "Download läuft — Statusbar im Hauptfenster zeigt Fortschritt.";
        await _main.DownloadFromDetailAsync(_modId, Title);
        StatusText = "Download angestoßen — sichtbar im Downloads-Tab.";
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
            StatusText = "Fehler: Browser konnte nicht geöffnet werden.";
        }
    }
}
