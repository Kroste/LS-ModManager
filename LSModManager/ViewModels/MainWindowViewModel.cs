using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Localization;
using LSModManager.Models;
using LSModManager.Services;
using NLog;

namespace LSModManager.ViewModels;

/// <summary>
/// ViewModel des Hauptfensters. Delegiert alles an Services — keine Datei- oder
/// Netzwerk-Logik hier drin (Kroste-Regel: ViewModels dünn halten).
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ModInstallService _install;
    private readonly ModBackupService _backup;
    private readonly ModPathService _paths;
    private readonly ModHubService _hub;
    private readonly ModhosterCatalogService _modhoster;
    private readonly HofHirschfeldCatalogService _hofHirschfeld;
    private readonly AppSettingsService _settings;
    private readonly UpdateService _updates;

    // Roh-Liste aller geladenen Katalog-Einträge (über alle Seiten hinweg).
    // Wird von einem Background-Task gefüllt und vom UI-Thread gelesen —
    // deswegen alle Zugriffe unter _catalogLock.
    private readonly object _catalogLock = new();
    private readonly List<ModHubEntry> _allCatalog = new();
    private int _lastLoadedPage;
    private bool _catalogReachedEnd;
    private CancellationTokenSource? _fullLoadCts;

    public MainWindowViewModel(
        ModInstallService install,
        ModBackupService backup,
        ModPathService paths,
        ModHubService hub,
        ModhosterCatalogService modhoster,
        HofHirschfeldCatalogService hofHirschfeld,
        AppSettingsService settings,
        UpdateService updates)
    {
        _install = install;
        _backup = backup;
        _paths = paths;
        _hub = hub;
        _modhoster = modhoster;
        _hofHirschfeld = hofHirschfeld;
        _settings = settings;
        _updates = updates;

        ModPath = _paths.GetModPath() ?? "";
        _statusText = L.T("Status_Ready");
        _selectedSortOption = SortOptions[0]; // Default: nach Name sortieren

        // Sprachwechsel im laufenden Betrieb: ModPathStatusText und CurrentVersionText
        // sind computed-Properties mit L.T-Aufrufen — die müssen bei Sprachwechsel neu
        // an die Bindings gemeldet werden. Alle transienten StatusText-Meldungen bleiben
        // bewusst in der Sprache, in der sie gesetzt wurden — sie werden bei der
        // nächsten User-Aktion sowieso überschrieben.
        LocalizationService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(LocalizationService.Current)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(ModPathStatusText));
                // Sentinel-Label der „Alle Kategorien"-Zeile aktualisieren, falls
                // die Kategorien schon geladen sind. Position 0 ist per Konvention
                // der Sentinel (siehe LoadCategoriesAsync / RefreshCatalogAsync).
                if (Categories.Count > 0 && string.IsNullOrEmpty(Categories[0].Filter))
                {
                    var wasSelected = ReferenceEquals(SelectedCategory, Categories[0]);
                    Categories[0] = CreateAllCategoriesSentinel();
                    if (wasSelected) SelectedCategory = Categories[0];
                }
            });
        };

        _ = RefreshInstalledAsync();
        _ = RefreshDownloadedAsync();
        // Beim Start: Cache instant anzeigen. Refresh-Button macht Full-Reload.
        LoadCatalogFromCacheOrRefresh();
    }

    /// <summary>
    /// Erzeugt eine frische Sentinel-Instanz mit lokalisiertem Label. Sentinel-
    /// Kennzeichen ist der leere <see cref="ModHubCategory.Filter"/> — Vergleiche
    /// laufen darüber, nicht per Referenz-Identität (siehe
    /// <see cref="EffectiveCategoryFilter"/>).
    /// </summary>
    private static ModHubCategory CreateAllCategoriesSentinel() =>
        new("", L.T("Catalog_AllCategories"));

    /// <summary>
    /// Beim App-Start: Cache-Datei lesen (falls vorhanden und frisch genug) und
    /// sofort anzeigen. Bei abgelaufenem Cache oder Erstinstallation läuft ein
    /// Full-Load.
    /// </summary>
    private void LoadCatalogFromCacheOrRefresh()
    {
        var lang = _settings.Current.CatalogLanguage ?? "de";
        var cached = CatalogCache.Load(lang);
        var maxAge = TimeSpan.FromHours(Math.Max(0, _settings.Current.CatalogRefreshHours));
        var cacheStale = cached is null
            || cached.Entries.Count == 0
            || DateTime.UtcNow - cached.SavedUtc > maxAge;

        if (cacheStale)
        {
            Log.Info("Katalog-Cache abgelaufen oder fehlend — starte Full-Load (maxAge={h}h).",
                _settings.Current.CatalogRefreshHours);
            _ = RefreshCatalogAsync();
            return;
        }

        lock (_catalogLock)
        {
            _allCatalog.Clear();
            _allCatalog.AddRange(cached!.Entries);
            _lastLoadedPage = -1;
            _catalogReachedEnd = true; // Cache ist vollständig — kein Auto-Load.
        }
        RebuildCatalogView();
        var age = DateTime.UtcNow - cached.SavedUtc;
        var ageText = age.TotalHours < 1
            ? $"{age.TotalMinutes:F0} min"
            : $"{age.TotalHours:F1} h";
        StatusText = L.F("Status_CatalogFromCache", cached.Entries.Count, ageText);

        // Kategorien werden nicht mit-gecacht (Sprach-abhängig, klein) — im
        // Hintergrund einmalig nachladen, damit der Filter-Dropdown gefüllt ist.
        _ = LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        if (Categories.Count > 0) return;
        try
        {
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var html = await _hub.FetchCatalogPageHtmlAsync(1, lang);
            if (html is null) return;
            var cats = ModHubService.ParseCategories(html);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Categories.Count > 0) return;
                Categories.Add(CreateAllCategoriesSentinel());
                foreach (var c in cats) Categories.Add(c);
                Log.Info("Kategorien nachgeladen: {n}", cats.Count);
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kategorien-Nachladen fehlgeschlagen");
        }
    }

    /// <summary>Signal an das MainWindow: bitte ein Detail-Fenster für diesen Mod öffnen.</summary>
    public event Action<ModHubItemViewModel>? DetailRequested;

    /// <summary>Rohliste aller installierten Mods (unfiltered). Wird für die Suche
    /// verwendet; <see cref="InstalledMods"/> ist die gefilterte Sicht.</summary>
    private readonly List<InstalledModItemViewModel> _allInstalled = new();

    public ObservableCollection<InstalledModItemViewModel> InstalledMods { get; } = new();
    public ObservableCollection<InstalledModItemViewModel> DownloadedMods { get; } = new();
    public ObservableCollection<ModHubItemViewModel> CatalogMods { get; } = new();

    [ObservableProperty]
    private InstalledModItemViewModel? _selectedInstalled;

    [ObservableProperty]
    private ModHubItemViewModel? _selectedCatalog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenModFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallFromZipCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallDownloadCommand))]
    private string _modPath = "";

    public string ModPathStatusText => string.IsNullOrWhiteSpace(ModPath)
        ? L.T("ModPath_NotFound")
        : L.T("ModPath_Found");

    /// <summary>
    /// Gesamtgröße aller aktivierten Mods (formatiert als MB/GB). Wird in der
    /// Statusbar rechts neben dem Zähler angezeigt — LS25 hat ein weiches Limit
    /// bei ~30 GB Mod-Volumen, da hilft die Zahl beim Ausmisten.
    /// </summary>
    public string TotalActiveSizeText
    {
        get
        {
            long bytes = 0;
            foreach (var m in _allInstalled)
                if (m.Model.IsEnabled) bytes += m.Model.FileSizeBytes;
            return FormatSize(bytes);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024d):F1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):F2} GB";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInstalledCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCatalogCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallFromZipCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckModUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDownloadedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateModCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Fortschrittswert (0..1) für die Statusbar. <c>null</c> = indeterminate
    /// (unbekannter Fortschritt, Animation). Wird aus den Progress-Reportern
    /// von Download/Backup/Restore/Katalog-Load gefeuert und muss NACH jeder
    /// Aktion zurückgesetzt werden (<c>ProgressValue = null</c>) — sonst hängt
    /// der Balken auf 100 %.
    /// </summary>
    [ObservableProperty]
    private double? _progressValue;

    /// <summary>True wenn <see cref="ProgressValue"/> auf einem konkreten Wert
    /// steht — an XAML gebunden um zwischen determinate und indeterminate
    /// umzuschalten.</summary>
    public bool HasProgressValue => ProgressValue.HasValue;

    /// <summary>True wenn eine Operation läuft (IsBusy), aber kein konkreter
    /// Fortschrittswert bekannt ist — dann zeigt die Statusbar die
    /// Indeterminate-Animation. Beispiel: „Lade installierte Mods …" (kurze
    /// Aktion ohne Progress-Reporter).</summary>
    public bool IsBusyIndeterminate => IsBusy && !ProgressValue.HasValue;

    partial void OnProgressValueChanged(double? value)
    {
        OnPropertyChanged(nameof(HasProgressValue));
        OnPropertyChanged(nameof(IsBusyIndeterminate));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsBusyIndeterminate));

    /// <summary>Filter-Text für den Katalog. Wird live angewandt (Titel/Autor/Kategorie).</summary>
    [ObservableProperty]
    private string _catalogSearchText = "";

    /// <summary>Filter-Text für installierte Mods. Live über Titel/Autor/Filename.</summary>
    [ObservableProperty]
    private string _installedSearchText = "";

    partial void OnInstalledSearchTextChanged(string value) => RebuildInstalledView();

    /// <summary>Filter „nur Mods mit verfügbarem Update" (aus dem Ergebnis von
    /// „Updates prüfen"). Standardmäßig aus.</summary>
    [ObservableProperty]
    private bool _showOnlyWithUpdate;

    partial void OnShowOnlyWithUpdateChanged(bool value) => RebuildInstalledView();

    /// <summary>Sortierung der Installiert-Liste. Standard: Name.</summary>
    public IReadOnlyList<InstalledSortOption> SortOptions { get; } = new[]
    {
        new InstalledSortOption(InstalledSortKey.Name, LocalizedString.Get("Installed_Sort_Name")),
        new InstalledSortOption(InstalledSortKey.Size, LocalizedString.Get("Installed_Sort_Size")),
        new InstalledSortOption(InstalledSortKey.Date, LocalizedString.Get("Installed_Sort_Date")),
        new InstalledSortOption(InstalledSortKey.Status, LocalizedString.Get("Installed_Sort_Status")),
    };

    [ObservableProperty]
    private InstalledSortOption? _selectedSortOption;

    partial void OnSelectedSortOptionChanged(InstalledSortOption? value) => RebuildInstalledView();

    /// <summary>Aktive GIANTS-Kategorie (Filter). Null = alle.</summary>
    [ObservableProperty]
    private ModHubCategory? _selectedCategory;

    public ObservableCollection<ModHubCategory> Categories { get; } = new();

    partial void OnSelectedCategoryChanged(ModHubCategory? value)
    {
        // „Alle Kategorien"-Sentinel behandelt der Filter als null (kein Filter).
        _ = RefreshCatalogAsync();
    }

    /// <summary>
    /// Der real an die URL angehängte Filter. Der „Alle Kategorien"-Sentinel hat
    /// einen leeren <see cref="ModHubCategory.Filter"/> — Vergleich per Filter-
    /// Inhalt, nicht per Referenz-Identität (Sentinel-Instanz kann bei Sprachwechsel
    /// ausgetauscht werden, damit sich das Label live aktualisiert).
    /// </summary>
    private string? EffectiveCategoryFilter =>
        (SelectedCategory is null || string.IsNullOrEmpty(SelectedCategory.Filter))
            ? null
            : SelectedCategory.Filter;

    partial void OnModPathChanged(string value) => OnPropertyChanged(nameof(ModPathStatusText));

    partial void OnCatalogSearchTextChanged(string value) => RebuildCatalogView();

    public string CurrentVersionText => $"v{_updates.CurrentVersion}";

    /// <summary>
    /// Nur relevant für den manuellen „Weitere laden"-Button (falls sichtbar).
    /// Der Auto-Full-Load läuft sowieso parallel im Hintergrund.
    /// </summary>
    public bool CanLoadMoreCatalog => !IsBusy && !_catalogReachedEnd && _allCatalog.Count > 0;

    // ---- Installed ----

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RefreshInstalledAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = L.T("Status_LoadingInstalled");
            var list = await Task.Run(() => _install.ListInstalled());
            _allInstalled.Clear();
            foreach (var m in list) _allInstalled.Add(new InstalledModItemViewModel(m));
            RebuildInstalledView();
            StatusText = L.F("Status_InstalledCount", _allInstalled.Count);
            OnPropertyChanged(nameof(TotalActiveSizeText));
            Log.Info("Installierte Mods aktualisiert: {n}", _allInstalled.Count);
            _ = BackfillCoversAsync(_allInstalled, isInstalled: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh installierter Mods fehlgeschlagen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>Baut die gefilterte + sortierte Installiert-Ansicht neu
    /// (bei Refresh, Suchtext-, Sort- oder Update-Filter-Wechsel).</summary>
    private void RebuildInstalledView()
    {
        var filter = InstalledSearchText?.Trim();
        IEnumerable<InstalledModItemViewModel> query = _allInstalled;

        if (!string.IsNullOrEmpty(filter))
            query = query.Where(m => MatchesInstalledFilter(m, filter));

        if (ShowOnlyWithUpdate)
            query = query.Where(m => m.HasUpdate);

        query = SortInstalled(query, SelectedSortOption?.Key ?? InstalledSortKey.Name);

        InstalledMods.Clear();
        foreach (var m in query) InstalledMods.Add(m);
    }

    private static IEnumerable<InstalledModItemViewModel> SortInstalled(
        IEnumerable<InstalledModItemViewModel> source, InstalledSortKey key) => key switch
    {
        InstalledSortKey.Size   => source.OrderByDescending(m => m.Model.FileSizeBytes),
        InstalledSortKey.Date   => source.OrderByDescending(m => m.Model.InstalledUtc),
        // Status: Aktive zuerst, dann deaktivierte — bei gleichem Status nach Name.
        InstalledSortKey.Status => source.OrderByDescending(m => m.Model.IsEnabled)
                                          .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
        _                       => source.OrderBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
    };

    private static bool MatchesInstalledFilter(InstalledModItemViewModel item, string filter) =>
        item.DisplayTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (item.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || item.Model.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanInstallFromZip))]
    public async Task InstallFromZipAsync(string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath)) return;
        try
        {
            IsBusy = true;
            StatusText = L.F("Status_Installing", Path.GetFileName(zipPath!));
            await Task.Run(() => _install.Install(zipPath!, overwrite: true));
            await RefreshInstalledAsync();
            StatusText = L.T("Status_ModInstalled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installation fehlgeschlagen: {p}", zipPath);
            StatusText = L.F("Status_InstallFailed", ex.Message);
        }
        finally { IsBusy = false; }
    }

    private bool CanInstallFromZip() => !IsBusy && !string.IsNullOrWhiteSpace(ModPath);

    // ---- Backup / Restore ----

    /// <summary>
    /// Sichert alle Mods aus dem Mod-Ordner (aktiv + deaktiviert) in ein
    /// selbstenthaltenes ZIP-Archiv am gewählten Zielpfad. Progress und
    /// Endstatus laufen über die Statusbar.
    /// </summary>
    public async Task CreateBackupAsync(string targetZipPath)
    {
        if (string.IsNullOrWhiteSpace(targetZipPath)) return;
        try
        {
            IsBusy = true;
            var progress = new Progress<BackupProgress>(p =>
            {
                StatusText = L.F("Status_BackupCreating", p.Current, p.Total, p.CurrentFileName);
                ProgressValue = p.Fraction;
            });
            var result = await _backup.CreateBackupAsync(targetZipPath, progress);
            var mb = result.FileSizeBytes / (1024d * 1024d);
            StatusText = L.F("Status_BackupCreated", result.ModCount, mb, result.FilePath);
        }
        catch (InvalidOperationException)
        {
            // Spezifisch: „keine Mods" — eigener Text statt generisches „Fehler".
            StatusText = L.T("Status_BackupNoMods");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup fehlgeschlagen: {p}", targetZipPath);
            StatusText = L.F("Status_BackupFailed", ex.Message);
        }
        finally { IsBusy = false; ProgressValue = null; }
    }

    /// <summary>
    /// Stellt ein Backup wieder her: liest das Manifest, entpackt die enthaltenen
    /// Mod-ZIPs in den Mod-Ordner und setzt den Enabled-State pro Mod aus dem
    /// Manifest. Bestehende Dateien werden überschrieben.
    /// </summary>
    public async Task RestoreBackupAsync(string backupZipPath)
    {
        if (string.IsNullOrWhiteSpace(backupZipPath)) return;
        try
        {
            IsBusy = true;
            StatusText = L.T("Status_RestoreReading");
            var progress = new Progress<BackupProgress>(p =>
            {
                StatusText = L.F("Status_Restoring", p.Current, p.Total, p.CurrentFileName);
                ProgressValue = p.Fraction;
            });
            var result = await _backup.RestoreBackupAsync(backupZipPath, progress);
            await RefreshInstalledAsync();
            StatusText = L.F("Status_RestoreDone", result.RestoredCount, result.SkippedCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore fehlgeschlagen: {p}", backupZipPath);
            StatusText = L.F("Status_RestoreFailed", ex.Message);
        }
        finally { IsBusy = false; ProgressValue = null; }
    }

    /// <summary>
    /// Installiert mehrere ZIPs am Stück (typisch: Drag-and-Drop von mehreren
    /// Dateien). Nicht-ZIPs und ungültige Mod-Archive werden übersprungen und
    /// gezählt, damit der Nutzer sieht was passiert ist.
    /// </summary>
    public async Task InstallZipsAsync(IReadOnlyList<string> zipPaths)
    {
        if (zipPaths.Count == 0 || string.IsNullOrWhiteSpace(ModPath)) return;
        var installed = 0;
        var skipped = 0;
        try
        {
            IsBusy = true;
            for (var i = 0; i < zipPaths.Count; i++)
            {
                var path = zipPaths[i];
                var name = Path.GetFileName(path);
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                StatusText = L.F("Status_InstallingProgress", i + 1, zipPaths.Count, name);
                try
                {
                    await Task.Run(() => _install.Install(path, overwrite: true));
                    installed++;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Drop-Install übersprungen: {p}", path);
                    skipped++;
                }
            }
            await RefreshInstalledAsync();
            StatusText = skipped == 0
                ? L.F("Status_BulkInstalled", installed)
                : L.F("Status_BulkInstalledWithSkipped", installed, skipped);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task UninstallAsync(InstalledModItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            StatusText = L.F("Status_Uninstalling", item.DisplayTitle);
            await Task.Run(() => _install.Uninstall(item.Model));
            await RefreshInstalledAsync();
            StatusText = L.T("Status_ModUninstalled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deinstallation fehlgeschlagen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ToggleEnabledAsync(InstalledModItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            var wantEnabled = !item.Model.IsEnabled;
            await Task.Run(() => _install.SetEnabled(item.Model, wantEnabled));
            await RefreshInstalledAsync();
            StatusText = L.T(wantEnabled ? "Status_ModEnabled" : "Status_ModDisabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Umschalten fehlgeschlagen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Bulk-Aktivierung/-Deaktivierung: alle gewählten Mods auf den gewünschten
    /// State bringen. Aus der UI mit der ListBox-SelectedItems-Liste aufgerufen.
    /// </summary>
    public async Task BulkSetEnabledAsync(IReadOnlyList<InstalledModItemViewModel> items, bool enable)
    {
        if (items.Count == 0) return;
        try
        {
            IsBusy = true;
            var changed = 0;
            foreach (var item in items)
            {
                if (item.Model.IsEnabled == enable) continue;
                try
                {
                    await Task.Run(() => _install.SetEnabled(item.Model, enable));
                    changed++;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Bulk-Toggle übersprungen: {p}", item.Model.FilePath);
                }
            }
            await RefreshInstalledAsync();
            StatusText = L.F(enable ? "Status_BulkEnabled" : "Status_BulkDisabled", changed);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Bulk-Deinstallation aller übergebenen Mods.</summary>
    public async Task BulkUninstallAsync(IReadOnlyList<InstalledModItemViewModel> items)
    {
        if (items.Count == 0) return;
        try
        {
            IsBusy = true;
            var removed = 0;
            foreach (var item in items)
            {
                try
                {
                    await Task.Run(() => _install.Uninstall(item.Model));
                    removed++;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Bulk-Uninstall übersprungen: {p}", item.Model.FilePath);
                }
            }
            await RefreshInstalledAsync();
            StatusText = L.F("Status_BulkUninstalled", removed);
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Startet LS25 via Steam-Protokoll. Funktioniert plattformübergreifend
    /// (Windows + Linux/Proton), sofern Steam installiert ist. AppID kommt aus
    /// den Settings, Fallback auf FS25-ID 2300320.
    /// </summary>
    [RelayCommand]
    public void LaunchGame()
    {
        try
        {
            var appId = _settings.Current.SteamAppId > 0 ? _settings.Current.SteamAppId : 2300320;
            var uri = $"steam://run/{appId}";
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            StatusText = L.T("Status_LaunchingGame");
            Log.Info("Spiel-Start: {uri}", uri);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Spiel konnte nicht gestartet werden");
            StatusText = L.F("Status_LaunchFailed", ex.Message);
        }
    }

    /// <summary>
    /// Prüft für jeden installierten Mod, ob im Katalog eine neuere Version
    /// gelistet ist. „Neuere Version" wird per Detail-Seite geholt (dort steht
    /// die vollständige Version), Vergleich per <see cref="System.Version"/>.
    /// Läuft nicht automatisch — der Nutzer klickt „Updates prüfen".
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task CheckModUpdatesAsync()
    {
        try
        {
            IsBusy = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var checkedCount = 0;
            var updatedCount = 0;
            foreach (var item in InstalledMods.ToList())
            {
                var current = item.Version;
                if (string.IsNullOrWhiteSpace(current)) continue;

                var coverUrl = LookupCoverUrl(item.Model.FileName);
                var catalogEntry = LookupCatalogEntry(item.Model.FileName);
                if (catalogEntry is null) continue;
                var modId = ExtractModIdFromUrl(catalogEntry.DetailUrl);
                if (modId is null) continue;

                checkedCount++;
                StatusText = L.F("Status_CheckingUpdates", checkedCount, item.DisplayTitle);
                var detail = await _hub.FetchModDetailAsync(modId.Value, lang);
                if (detail is null || string.IsNullOrWhiteSpace(detail.Version)) continue;

                if (IsVersionNewer(detail.Version, current))
                {
                    item.SetUpdateAvailable(detail.Version);
                    updatedCount++;
                    Log.Info("Update verfügbar: {t} {cur} → {new}", item.DisplayTitle, current, detail.Version);
                }
            }
            StatusText = updatedCount > 0
                ? L.F("Status_UpdatesFound", updatedCount, checkedCount)
                : L.F("Status_UpdatesNoneFound", checkedCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Prüfung fehlgeschlagen");
            StatusText = L.F("Status_UpdateCheckFailed", ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Führt das eigentliche Update aus: lädt die neue Version vom Katalog,
    /// deinstalliert die alte, installiert die neue, überträgt den Enabled-State.
    /// Voraussetzung: <see cref="InstalledModItemViewModel.HasUpdate"/> ist true
    /// (per <see cref="CheckModUpdatesAsync"/> gesetzt) und Katalog-Entry vorhanden.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateMod))]
    public async Task UpdateModAsync(InstalledModItemViewModel? item)
    {
        if (item is null || !item.HasUpdate) return;
        var catalogEntry = LookupCatalogEntry(item.Model.FileName);
        if (catalogEntry is null)
        {
            StatusText = L.T("Status_UpdateNoCatalogEntry");
            return;
        }
        var modId = ExtractModIdFromUrl(catalogEntry.DetailUrl);
        if (modId is null) return;
        if (string.IsNullOrWhiteSpace(ModPath))
        {
            StatusText = L.T("Status_UpdateNoModPath");
            return;
        }

        try
        {
            IsBusy = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var wasEnabled = item.Model.IsEnabled;
            var oldTitle = item.DisplayTitle;
            var oldVersion = item.Version;

            var progress = new Progress<ModDownloadProgress>(p =>
            {
                StatusText = L.F("Status_UpdateDownloading", oldTitle,
                    p.FormatShort() + (p.Fraction is { } f ? $" ({f * 100:F0}%)" : ""));
                ProgressValue = p.Fraction;
            });

            // 1. Neue Version in den Downloads-Ordner laden (Cover mit).
            var result = await _hub.DownloadModAsync(modId.Value, lang, progress,
                coverImageUrl: string.IsNullOrWhiteSpace(catalogEntry.PreviewUrl)
                    ? null : catalogEntry.PreviewUrl);

            // 2. Alte Version aus dem Mod-Ordner entfernen.
            StatusText = L.F("Status_UpdateReplacing", oldTitle);
            await Task.Run(() => _install.Uninstall(item.Model));

            // 3. Neue Version aus Downloads-Ordner installieren (kopiert in Mod-Ordner).
            var newMod = await Task.Run(() => _install.Install(result.TargetZipPath, overwrite: true));

            // 4. Enabled-State übertragen: war die alte deaktiviert, deaktivieren wir
            //    die neue ebenfalls.
            if (!wasEnabled)
                await Task.Run(() => _install.SetEnabled(newMod, false));

            await RefreshInstalledAsync();
            await RefreshDownloadedAsync();
            StatusText = L.F("Status_UpdateInstalled", oldTitle, oldVersion,
                item.UpdateAvailableVersion ?? "");
            Log.Info("Update installiert: {t}", oldTitle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Installation fehlgeschlagen");
            StatusText = L.F("Status_UpdateFailed", ex.Message);
        }
        finally { IsBusy = false; ProgressValue = null; }
    }

    private bool CanUpdateMod() => !IsBusy && !string.IsNullOrWhiteSpace(ModPath);

    /// <summary>Semver-Vergleich (auch 4-teilig wie „8.1.0.3"); Fehler = kein Update.</summary>
    public static bool IsVersionNewer(string catalogVersion, string installedVersion)
    {
        if (!Version.TryParse(catalogVersion.Trim(), out var cat)) return false;
        if (!Version.TryParse(installedVersion.Trim(), out var inst)) return false;
        return cat > inst;
    }

    private ModHubEntry? LookupCatalogEntry(string zipFileName)
    {
        var normalized = NormalizeForMatch(Path.GetFileNameWithoutExtension(zipFileName));
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        lock (_catalogLock)
        {
            foreach (var e in _allCatalog)
            {
                var titleNorm = NormalizeForMatch(e.Title);
                if (titleNorm.Length < 3) continue;
                if (normalized.Contains(titleNorm) || titleNorm.Contains(normalized))
                    return e;
            }
        }
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    public void OpenModFolder()
    {
        try
        {
            var path = _paths.GetModPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Mod-Ordner nicht öffnen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
    }

    private bool CanOpenModFolder() => !string.IsNullOrWhiteSpace(ModPath);

    // ---- Downloads (persistenter Ordner) ----

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RefreshDownloadedAsync()
    {
        try
        {
            var list = await Task.Run(() => _install.ListDownloaded());
            DownloadedMods.Clear();
            foreach (var m in list) DownloadedMods.Add(new InstalledModItemViewModel(m));
            Log.Info("Downloads aktualisiert: {n}", DownloadedMods.Count);
            _ = BackfillCoversAsync(DownloadedMods, isInstalled: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh Downloads fehlgeschlagen");
        }
    }

    /// <summary>
    /// Für Mods ohne Preview-Bild: schaut im geladenen Katalog nach einem Match
    /// per ZIP-Filename und lädt das Cover asynchron nach. Nach erfolgreichem
    /// Backfill wird die entsprechende Collection neu aufgebaut, damit die
    /// Bindings die neue Datei sehen. Läuft im Hintergrund — blockiert nicht.
    /// </summary>
    private async Task BackfillCoversAsync(
        IEnumerable<InstalledModItemViewModel> collection, bool isInstalled)
    {
        try
        {
            // Nicht nur „Preview null" — auch wenn schon ein ZIP-icon.png-Cache
            // existiert, aber kein Katalog-Cover, wollen wir das bessere CDN-Bild
            // holen (Modhoster/Hof-Hirschfeld-Downloads haben kein Auto-Cover).
            var itemsWithoutCatalogCover = collection
                .Where(i => !AppPaths.HasCatalogCoverCache(i.Model.FilePath))
                .Select(i => i.Model.FilePath)
                .ToList();
            if (itemsWithoutCatalogCover.Count == 0) return;

            var anyLoaded = false;
            foreach (var zipPath in itemsWithoutCatalogCover)
            {
                var coverUrl = LookupCoverUrl(Path.GetFileName(zipPath));
                if (string.IsNullOrWhiteSpace(coverUrl)) continue;
                var cached = await _hub.EnsureCoverCachedAsync(zipPath, coverUrl);
                if (cached is not null) anyLoaded = true;
            }

            if (anyLoaded)
            {
                // Neuaufbau nur der betroffenen Collection — die Bindings zeigen
                // jetzt das nachgeladene Preview.
                if (isInstalled) await RefreshInstalledAsync();
                else await RefreshDownloadedAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Cover-Backfill fehlgeschlagen");
        }
    }

    /// <summary>
    /// Findet die Cover-URL für einen ZIP-Filename im geladenen Katalog. Match
    /// ist fuzzy: Titel-Wörter werden mit dem Filename verglichen (case- und
    /// separator-unabhängig). Ohne Katalog: null.
    /// </summary>
    private string? LookupCoverUrl(string zipFileName)
    {
        var normalized = NormalizeForMatch(Path.GetFileNameWithoutExtension(zipFileName));
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        // Sammle ALLE Kandidaten, dann kürzesten Titel wählen. „AutoDrive"
        // (Basis-Mod) gewinnt so über „AutoDrive Yagodnoye Village Beta"
        // (Varianten-Mod) — der kürzeste Titel ist typischerweise der
        // generische Eintrag, den der User haben will.
        List<ModHubEntry> candidates = new();
        lock (_catalogLock)
        {
            foreach (var e in _allCatalog)
            {
                if (string.IsNullOrWhiteSpace(e.PreviewUrl)) continue;
                var titleNorm = NormalizeForMatch(e.Title);
                if (titleNorm.Length < 3) continue;
                if (normalized.Contains(titleNorm) || titleNorm.Contains(normalized))
                    candidates.Add(e);
            }
        }
        return candidates
            .OrderBy(e => e.Title.Length)
            .FirstOrDefault()
            ?.PreviewUrl;
    }

    private static string NormalizeForMatch(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        var result = sb.ToString();
        // Verbreitete Präfixe abschneiden — sie stören das Contains-Match.
        foreach (var prefix in new[] { "fs25", "fs22", "ls25", "ls22" })
            if (result.StartsWith(prefix)) result = result.Substring(prefix.Length);
        return result;
    }

    [RelayCommand]
    public void OpenDownloadsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DownloadsDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Downloads-Ordner nicht öffnen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallDownload))]
    public async Task InstallDownloadAsync(InstalledModItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            StatusText = L.F("Status_Installing", item.DisplayTitle);
            await Task.Run(() => _install.Install(item.Model.FilePath, overwrite: true));
            await RefreshInstalledAsync();
            StatusText = L.F("Status_DownloadInstalled", item.DisplayTitle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Install-Download fehlgeschlagen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private bool CanInstallDownload() => !IsBusy && !string.IsNullOrWhiteSpace(ModPath);

    [RelayCommand]
    public async Task DeleteDownloadAsync(InstalledModItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            await Task.Run(() => _install.DeleteDownload(item.Model.FilePath));
            await RefreshDownloadedAsync();
            StatusText = L.F("Status_DownloadDeleted", item.Model.FileName);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Download nicht löschen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
    }

    // ---- Katalog: Refresh, LoadMore, Suche, Download, Details ----

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RefreshCatalogAsync()
    {
        try
        {
            // Alten Background-Load abbrechen (falls noch aktiv).
            _fullLoadCts?.Cancel();
            _fullLoadCts?.Dispose();
            _fullLoadCts = new CancellationTokenSource();

            IsBusy = true;
            StatusText = L.T("Status_CatalogLoadingPage1");
            lock (_catalogLock)
            {
                _allCatalog.Clear();
                _lastLoadedPage = 0;
                _catalogReachedEnd = false;
            }

            var lang = _settings.Current.CatalogLanguage ?? "de";
            var filter = EffectiveCategoryFilter;
            // Beim ersten Load: HTML komplett holen, um Kategorien zu extrahieren.
            var html = await _hub.FetchCatalogPageHtmlAsync(1, lang, filter: filter);
            IReadOnlyList<ModHubEntry> entries;
            if (html is not null)
            {
                entries = ModHubService.ParseListPage(html);
                if (Categories.Count == 0)
                {
                    var cats = ModHubService.ParseCategories(html);
                    Categories.Add(CreateAllCategoriesSentinel());
                    foreach (var c in cats) Categories.Add(c);
                    Log.Info("Kategorien geladen: {n}", cats.Count);
                }
            }
            else
            {
                entries = Array.Empty<ModHubEntry>();
            }
            lock (_catalogLock)
            {
                _allCatalog.AddRange(entries);
                _lastLoadedPage = 1;
                _catalogReachedEnd = entries.Count == 0;
            }

            RebuildCatalogView();
            OnPropertyChanged(nameof(CanLoadMoreCatalog));

            if (_catalogReachedEnd)
            {
                StatusText = L.T("Status_CatalogEmpty");
                return;
            }

            StatusText = L.F("Status_CatalogPage1Loaded", _allCatalog.Count);
            // Alle drei Katalog-Quellen parallel im Hintergrund. GIANTS ist am
            // langsamsten (~1-2 min HTML-Scraping), Modhoster ist JSON und schnell,
            // Hof-Hirschfeld ist kleiner Community-Katalog (~30 Kategorien).
            _ = LoadAllRemainingPagesAsync(_fullLoadCts.Token);
            _ = LoadModhosterCatalogAsync(_fullLoadCts.Token);
            _ = LoadHofHirschfeldCatalogAsync(_fullLoadCts.Token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Katalog-Refresh fehlgeschlagen");
            StatusText = L.F("Status_CatalogError", ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Lädt Seite für Seite alle restlichen Katalog-Seiten mit kleinem Delay
    /// (Rate-Limit-Schonung) und aktualisiert die Ansicht inkrementell. So
    /// funktioniert Suche automatisch über den kompletten Katalog, sobald er
    /// vollständig gecacht ist — GIANTS hat keinen search-Parameter, deshalb ist
    /// clientseitig sammeln + filtern der einzige Weg.
    /// </summary>
    private async Task LoadAllRemainingPagesAsync(CancellationToken ct)
    {
        var lang = _settings.Current.CatalogLanguage ?? "de";
        var filter = EffectiveCategoryFilter;
        // GIANTS-Katalog hat aktuell ~200 Seiten; hartes Limit gegen Runaway.
        const int maxPages = 300;

        try
        {
            while (!ct.IsCancellationRequested && !_catalogReachedEnd && _lastLoadedPage < maxPages)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                var nextPage = _lastLoadedPage + 1;
                var entries = await _hub.FetchCatalogPageAsync(nextPage, lang, ct, filter)
                    .ConfigureAwait(false);

                var newlyAdded = new List<ModHubEntry>();
                int total, currentPage;
                bool reachedEnd;
                lock (_catalogLock)
                {
                    _lastLoadedPage = nextPage;
                    if (entries.Count == 0)
                    {
                        _catalogReachedEnd = true;
                    }
                    else
                    {
                        var existingUrls = new HashSet<string>(_allCatalog.Select(e => e.DetailUrl));
                        foreach (var e in entries)
                        {
                            if (existingUrls.Add(e.DetailUrl))
                            {
                                _allCatalog.Add(e);
                                newlyAdded.Add(e);
                            }
                        }
                    }
                    total = _allCatalog.Count;
                    currentPage = _lastLoadedPage;
                    reachedEnd = _catalogReachedEnd;
                }

                if (reachedEnd) break;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AppendToCatalogView(newlyAdded);
                    StatusText = L.F("Status_CatalogPageLoaded", total, currentPage);
                });

                // Inkrementeller Cache alle 10 Seiten — überlebt App-Crash / Close
                // in der Mitte des Full-Loads.
                if (currentPage % 10 == 0)
                    SaveCatalogSnapshot(lang);
            }

            if (!ct.IsCancellationRequested)
            {
                int finalTotal, finalPage;
                lock (_catalogLock) { finalTotal = _allCatalog.Count; finalPage = _lastLoadedPage; }
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = L.F("Status_CatalogGiantsComplete", finalTotal, finalPage);
                });
                Log.Info("GIANTS-Katalog-Full-Load fertig: {n} Einträge, {p} Seiten", finalTotal, finalPage);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Info("Katalog-Full-Load abgebrochen (Refresh neu gestartet oder App-Ende).");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-Full-Load abgebrochen mit Fehler");
        }
        finally
        {
            // Auch bei Cancellation/Fehler: den bisher gesammelten Stand cachen —
            // besser einen Teilcache als beim nächsten Start alles neu laden.
            SaveCatalogSnapshot(lang);
        }
    }

    /// <summary>
    /// Lädt sequenziell alle Modhoster-Katalog-Seiten (game_id=1 = LS25) und
    /// mischt die Einträge in <see cref="_allCatalog"/>. Modhoster-Einträge
    /// haben <c>CanInAppDownload=false</c> — die UI zeigt nur „🌐 Öffnen".
    /// </summary>
    private async Task LoadModhosterCatalogAsync(CancellationToken ct)
    {
        try
        {
            const int maxPages = 500; // Sicherheitsgrenze; API liefert ab ~200 leer
            int page = 1;
            var totalAdded = 0;
            while (!ct.IsCancellationRequested && page <= maxPages)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                var entries = await _modhoster.FetchCatalogPageAsync(page, ct).ConfigureAwait(false);
                if (entries.Count == 0) break;

                var newlyAdded = new List<ModHubEntry>();
                int total;
                lock (_catalogLock)
                {
                    var existingUrls = new HashSet<string>(_allCatalog.Select(e => e.DetailUrl));
                    foreach (var e in entries)
                    {
                        if (existingUrls.Add(e.DetailUrl))
                        {
                            _allCatalog.Add(e);
                            newlyAdded.Add(e);
                        }
                    }
                    totalAdded += newlyAdded.Count;
                    total = _allCatalog.Count;
                }

                var currentPage = page;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AppendToCatalogView(newlyAdded);
                    StatusText = L.F("Status_CatalogModhosterPage", total, currentPage);
                });

                if (currentPage % 10 == 0)
                    SaveCatalogSnapshot(_settings.Current.CatalogLanguage ?? "de");
                page++;
            }

            int finalTotal;
            lock (_catalogLock) finalTotal = _allCatalog.Count;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = L.F("Status_CatalogModhosterComplete", finalTotal);
            });
            Log.Info("Modhoster-Full-Load fertig: +{n} neue, {t} gesamt", totalAdded, finalTotal);
        }
        catch (OperationCanceledException) { /* ok */ }
        catch (Exception ex) { Log.Warn(ex, "Modhoster-Full-Load Fehler"); }
    }

    /// <summary>
    /// Lädt den kompletten hof-hirschfeld.de-Katalog: pro Kategorie alle
    /// Seiten. Mischt die Einträge in <see cref="_allCatalog"/>. Alle Cards
    /// haben <c>CanInAppDownload=false</c> — die Site verlangt Werbung-Consent
    /// für Downloads.
    /// </summary>
    private async Task LoadHofHirschfeldCatalogAsync(CancellationToken ct)
    {
        try
        {
            var slugs = await _hofHirschfeld.FetchCategorySlugsAsync(ct).ConfigureAwait(false);
            var totalAdded = 0;
            foreach (var slug in slugs)
            {
                if (ct.IsCancellationRequested) break;
                // Erste Seite → auch pagination-Analyse extrahieren.
                var page = 1;
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                    var entries = await _hofHirschfeld.FetchCategoryPageAsync(slug, page, ct)
                        .ConfigureAwait(false);
                    if (entries.Count == 0) break;

                    var newlyAdded = new List<ModHubEntry>();
                    int total;
                    lock (_catalogLock)
                    {
                        var existingUrls = new HashSet<string>(_allCatalog.Select(e => e.DetailUrl));
                        foreach (var e in entries)
                        {
                            if (existingUrls.Add(e.DetailUrl))
                            {
                                _allCatalog.Add(e);
                                newlyAdded.Add(e);
                            }
                        }
                        totalAdded += newlyAdded.Count;
                        total = _allCatalog.Count;
                    }

                    var currentPage = page;
                    var currentSlug = slug;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AppendToCatalogView(newlyAdded);
                        StatusText = L.F("Status_CatalogHofHirschfeldPage", total, currentSlug, currentPage);
                    });

                    // Nur weiter blättern wenn Seite voll war (12 pro Seite typisch).
                    if (entries.Count < 12) break;
                    page++;
                    if (page > 20) break; // Safety-Limit pro Kategorie
                }
            }

            int finalTotal;
            lock (_catalogLock) finalTotal = _allCatalog.Count;
            Log.Info("Hof-Hirschfeld-Full-Load fertig: +{n} neue, {t} gesamt", totalAdded, finalTotal);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                StatusText = L.F("Status_CatalogAllComplete", finalTotal));
        }
        catch (OperationCanceledException) { /* ok */ }
        catch (Exception ex) { Log.Warn(ex, "Hof-Hirschfeld-Full-Load Fehler"); }
    }

    private void SaveCatalogSnapshot(string language)
    {
        try
        {
            List<ModHubEntry> snapshot;
            lock (_catalogLock) { snapshot = _allCatalog.ToList(); }
            if (snapshot.Count > 0) CatalogCache.Save(snapshot, language);
        }
        catch (Exception ex) { Log.Warn(ex, "Katalog-Cache-Save fehlgeschlagen"); }
    }

    /// <summary>
    /// Vollständiger Neuaufbau der Katalog-Ansicht (nur bei Suchtext-Änderung
    /// oder Refresh). Bei laufendem Full-Load nutzen wir <see cref="AppendToCatalogView"/>,
    /// sonst flimmert die ListBox bei jedem Seiten-Nachlader.
    /// </summary>
    private void RebuildCatalogView()
    {
        List<ModHubEntry> snapshot;
        lock (_catalogLock) { snapshot = _allCatalog.ToList(); }

        CatalogMods.Clear();
        foreach (var e in snapshot.Where(MatchesFilter))
            CatalogMods.Add(new ModHubItemViewModel(e));
    }

    /// <summary>Nur die neuen Einträge anhängen — für den Background-Full-Load.</summary>
    private void AppendToCatalogView(IEnumerable<ModHubEntry> entries)
    {
        foreach (var e in entries.Where(MatchesFilter))
            CatalogMods.Add(new ModHubItemViewModel(e));
    }

    private bool MatchesFilter(ModHubEntry e)
    {
        var filter = CatalogSearchText?.Trim();
        if (string.IsNullOrEmpty(filter)) return true;
        return e.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || e.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || e.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Öffnet das Detail-Fenster für einen installierten Mod, wenn im Katalog
    /// ein Match gefunden wird. Ohne Match: null — der Caller entscheidet was
    /// zu tun ist (typisch: gar nichts, weil der Nutzer den Doppelklick
    /// verstehen wird). Wird vom Doppelklick-Handler auf der Installiert-Liste
    /// aufgerufen.
    /// </summary>
    public bool TryShowInstalledDetails(InstalledModItemViewModel? mod)
    {
        if (mod is null) return false;
        var entry = LookupCatalogEntry(mod.Model.FileName);
        if (entry is null) return false;
        DetailRequested?.Invoke(new ModHubItemViewModel(entry));
        return true;
    }

    /// <summary>Zeigt den Detail-Dialog für den übergebenen Mod (Signal an MainWindow).</summary>
    [RelayCommand]
    public void ShowDetails(ModHubItemViewModel? item)
    {
        if (item is null) return;
        // Modhoster hat keine eigene Detail-API — direkt im Browser öffnen.
        if (item.NeedsBrowser) { OpenInBrowser(item); return; }
        DetailRequested?.Invoke(item);
    }

    /// <summary>Öffnet die Katalog-Detail-URL im System-Browser.</summary>
    [RelayCommand]
    public void OpenInBrowser(ModHubItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DetailUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.DetailUrl) { UseShellExecute = true });
            StatusText = L.F("Status_BrowserOpened", item.Title);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Browser nicht öffnen");
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
    }

    /// <summary>
    /// Lädt die ZIP direkt vom GIANTS-CDN in den persistenten Downloads-Ordner.
    /// Installiert NICHT — das macht der Nutzer explizit im Downloads-Tab.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    public async Task DownloadAsync(ModHubItemViewModel? item)
    {
        var target = item ?? SelectedCatalog;
        if (target is null) return;
        var modId = ExtractModIdFromUrl(target.DetailUrl);
        if (modId is null)
        {
            StatusText = L.T("Status_DownloadNoModId");
            return;
        }

        try
        {
            IsBusy = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var progress = new Progress<ModDownloadProgress>(p =>
            {
                StatusText = L.F("Status_Downloading", target.Title,
                    p.FormatShort() + (p.Fraction is { } f ? $" ({f * 100:F0}%)" : ""));
                ProgressValue = p.Fraction;
            });
            var result = await _hub.DownloadModAsync(modId.Value, lang, progress,
                coverImageUrl: string.IsNullOrWhiteSpace(target.PreviewUrl) ? null : target.PreviewUrl);
            await RefreshDownloadedAsync();
            StatusText = L.F("Status_Downloaded", result.FileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download fehlgeschlagen für {title}", target.Title);
            StatusText = L.T("Common_ErrorPrefix") + ex.Message;
        }
        finally { IsBusy = false; ProgressValue = null; }
    }

    private bool CanDownload() => !IsBusy;

    private bool NotBusy() => !IsBusy;

    private static int? ExtractModIdFromUrl(string url)
    {
        var m = Regex.Match(url, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>Nach Änderungen von ModPath (z.B. aus Settings): Anzeige aktualisieren.</summary>
    public void ReloadPath()
    {
        ModPath = _paths.GetModPath() ?? "";
        _ = RefreshInstalledAsync();
    }

    /// <summary>Nutzt das Detail-Window fürs sofortige Download-Trigger.</summary>
    public Task DownloadFromDetailAsync(int modId, string title)
    {
        // Cover-URL aus dem Katalog-Cache holen, damit der Download ein Bild bekommt.
        ModHubEntry? found;
        lock (_catalogLock)
        {
            found = _allCatalog.FirstOrDefault(e => e.DetailUrl.Contains($"mod_id={modId}"));
        }
        var previewUrl = found?.PreviewUrl ?? "";
        var pseudo = new ModHubItemViewModel(new ModHubEntry(
            title, "", "", previewUrl,
            $"https://www.farming-simulator.com/mod.php?mod_id={modId}",
            null, null));
        return DownloadAsync(pseudo);
    }
}
