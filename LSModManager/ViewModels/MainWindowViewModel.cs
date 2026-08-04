using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ModPathService paths,
        ModHubService hub,
        ModhosterCatalogService modhoster,
        HofHirschfeldCatalogService hofHirschfeld,
        AppSettingsService settings,
        UpdateService updates)
    {
        _install = install;
        _paths = paths;
        _hub = hub;
        _modhoster = modhoster;
        _hofHirschfeld = hofHirschfeld;
        _settings = settings;
        _updates = updates;

        ModPath = _paths.GetModPath() ?? "";
        _ = RefreshInstalledAsync();
        _ = RefreshDownloadedAsync();
        // Beim Start: Cache instant anzeigen. Refresh-Button macht Full-Reload.
        LoadCatalogFromCacheOrRefresh();
    }

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
        StatusText = $"Katalog aus Cache: {cached.Entries.Count} Einträge (vor {ageText}). ↺ für Frische.";

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
                Categories.Add(AllCategoriesSentinel);
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
        ? "✗ Mod-Ordner nicht gefunden"
        : "✓ Mod-Ordner gefunden";

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
    private string _statusText = "Bereit.";

    /// <summary>Filter-Text für den Katalog. Wird live angewandt (Titel/Autor/Kategorie).</summary>
    [ObservableProperty]
    private string _catalogSearchText = "";

    /// <summary>Filter-Text für installierte Mods. Live über Titel/Autor/Filename.</summary>
    [ObservableProperty]
    private string _installedSearchText = "";

    partial void OnInstalledSearchTextChanged(string value) => RebuildInstalledView();

    /// <summary>Aktive GIANTS-Kategorie (Filter). Null = alle.</summary>
    [ObservableProperty]
    private ModHubCategory? _selectedCategory;

    public ObservableCollection<ModHubCategory> Categories { get; } = new();

    /// <summary>„Alle Kategorien"-Sentinel für die ComboBox.</summary>
    public static readonly ModHubCategory AllCategoriesSentinel = new("", "Alle Kategorien");

    partial void OnSelectedCategoryChanged(ModHubCategory? value)
    {
        // „Alle Kategorien"-Sentinel behandelt der Filter als null (kein Filter).
        _ = RefreshCatalogAsync();
    }

    /// <summary>Der real an die URL angehängte Filter (Sentinel = leer).</summary>
    private string? EffectiveCategoryFilter =>
        (SelectedCategory is null || SelectedCategory == AllCategoriesSentinel ||
         string.IsNullOrEmpty(SelectedCategory.Filter))
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
            StatusText = "Lade installierte Mods …";
            var list = await Task.Run(() => _install.ListInstalled());
            _allInstalled.Clear();
            foreach (var m in list) _allInstalled.Add(new InstalledModItemViewModel(m));
            RebuildInstalledView();
            StatusText = $"{_allInstalled.Count} Mods installiert.";
            Log.Info("Installierte Mods aktualisiert: {n}", _allInstalled.Count);
            _ = BackfillCoversAsync(_allInstalled, isInstalled: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh installierter Mods fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Baut die gefilterte Installiert-Ansicht neu (bei Refresh oder Suchtext-Wechsel).</summary>
    private void RebuildInstalledView()
    {
        var filter = InstalledSearchText?.Trim();
        InstalledMods.Clear();
        foreach (var m in _allInstalled)
        {
            if (string.IsNullOrEmpty(filter) || MatchesInstalledFilter(m, filter))
                InstalledMods.Add(m);
        }
    }

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
            StatusText = $"Installiere {Path.GetFileName(zipPath)} …";
            await Task.Run(() => _install.Install(zipPath!, overwrite: true));
            await RefreshInstalledAsync();
            StatusText = "Mod installiert.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installation fehlgeschlagen: {p}", zipPath);
            StatusText = $"Installation fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanInstallFromZip() => !IsBusy && !string.IsNullOrWhiteSpace(ModPath);

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
                StatusText = $"Installiere ({i + 1}/{zipPaths.Count}): {name} …";
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
                ? $"✓ {installed} Mod(s) installiert."
                : $"✓ {installed} installiert, {skipped} übersprungen (siehe Log).";
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
            StatusText = $"Deinstalliere {item.DisplayTitle} …";
            await Task.Run(() => _install.Uninstall(item.Model));
            await RefreshInstalledAsync();
            StatusText = "Mod deinstalliert.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deinstallation fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = wantEnabled ? "Mod aktiviert." : "Mod deaktiviert.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Umschalten fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = enable
                ? $"✓ {changed} Mod(s) aktiviert."
                : $"✓ {changed} Mod(s) deaktiviert.";
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
            StatusText = $"✓ {removed} Mod(s) deinstalliert.";
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
            StatusText = "Landwirtschafts-Simulator wird über Steam gestartet …";
            Log.Info("Spiel-Start: {uri}", uri);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Spiel konnte nicht gestartet werden");
            StatusText = $"Fehler beim Start: {ex.Message}";
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
                StatusText = $"Prüfe Updates ({checkedCount}) — {item.DisplayTitle} …";
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
                ? $"✓ {updatedCount} von {checkedCount} Mods haben Updates."
                : $"Keine Updates gefunden ({checkedCount} geprüft).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Prüfung fehlgeschlagen");
            StatusText = $"Fehler bei Update-Prüfung: {ex.Message}";
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
            StatusText = "Kein Katalog-Eintrag für Update gefunden.";
            return;
        }
        var modId = ExtractModIdFromUrl(catalogEntry.DetailUrl);
        if (modId is null) return;
        if (string.IsNullOrWhiteSpace(ModPath))
        {
            StatusText = "Mod-Ordner nicht gesetzt — in Einstellungen konfigurieren.";
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
                StatusText = $"⬇ Update {oldTitle}: {p.FormatShort()}"
                    + (p.Fraction is { } f ? $" ({f * 100:F0}%)" : ""));

            // 1. Neue Version in den Downloads-Ordner laden (Cover mit).
            var result = await _hub.DownloadModAsync(modId.Value, lang, progress,
                coverImageUrl: string.IsNullOrWhiteSpace(catalogEntry.PreviewUrl)
                    ? null : catalogEntry.PreviewUrl);

            // 2. Alte Version aus dem Mod-Ordner entfernen.
            StatusText = $"Ersetze {oldTitle} …";
            await Task.Run(() => _install.Uninstall(item.Model));

            // 3. Neue Version aus Downloads-Ordner installieren (kopiert in Mod-Ordner).
            var newMod = await Task.Run(() => _install.Install(result.TargetZipPath, overwrite: true));

            // 4. Enabled-State übertragen: war die alte deaktiviert, deaktivieren wir
            //    die neue ebenfalls.
            if (!wasEnabled)
                await Task.Run(() => _install.SetEnabled(newMod, false));

            await RefreshInstalledAsync();
            await RefreshDownloadedAsync();
            StatusText = $"✓ Update installiert: {oldTitle} ({oldVersion} → {item.UpdateAvailableVersion ?? "neu"})";
            Log.Info("Update installiert: {t}", oldTitle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Installation fehlgeschlagen");
            StatusText = $"Update-Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
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
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = $"Fehler: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallDownload))]
    public async Task InstallDownloadAsync(InstalledModItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            StatusText = $"Installiere {item.DisplayTitle} …";
            await Task.Run(() => _install.Install(item.Model.FilePath, overwrite: true));
            await RefreshInstalledAsync();
            StatusText = $"✓ Installiert: {item.DisplayTitle}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Install-Download fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = $"Gelöscht: {item.Model.FileName}";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Download nicht löschen");
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = "Lade ModHub-Katalog (Seite 1) …";
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
                    Categories.Add(AllCategoriesSentinel);
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
                StatusText = "Katalog leer oder nicht erreichbar (siehe Log).";
                return;
            }

            StatusText = $"Katalog: {_allCatalog.Count} Einträge (Seite 1), Rest wird nachgeladen …";
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
            StatusText = $"Katalog-Fehler: {ex.Message}";
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
                    StatusText = $"Katalog: {total} Einträge (Seite {currentPage} geladen) …";
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
                    StatusText = $"GIANTS-Katalog vollständig: {finalTotal} Einträge auf {finalPage} Seiten.";
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
                    StatusText = $"Katalog: {total} (Modhoster-Seite {currentPage}) …";
                });

                if (currentPage % 10 == 0)
                    SaveCatalogSnapshot(_settings.Current.CatalogLanguage ?? "de");
                page++;
            }

            int finalTotal;
            lock (_catalogLock) finalTotal = _allCatalog.Count;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Katalog vollständig: {finalTotal} Einträge (GIANTS + Modhoster).";
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
                        StatusText = $"Katalog: {total} (Hof Hirschfeld: {currentSlug} S{currentPage}) …";
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
                StatusText = $"Katalog: {finalTotal} Einträge (GIANTS + Modhoster + Hof Hirschfeld).");
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
            StatusText = $"Browser geöffnet: {item.Title}";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Browser nicht öffnen");
            StatusText = $"Fehler: {ex.Message}";
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
            StatusText = "Konnte mod_id nicht aus der URL lesen.";
            return;
        }

        try
        {
            IsBusy = true;
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var progress = new Progress<ModDownloadProgress>(p =>
                StatusText = $"⬇ {target.Title}: {p.FormatShort()}"
                    + (p.Fraction is { } f ? $" ({f * 100:F0}%)" : ""));
            var result = await _hub.DownloadModAsync(modId.Value, lang, progress,
                coverImageUrl: string.IsNullOrWhiteSpace(target.PreviewUrl) ? null : target.PreviewUrl);
            await RefreshDownloadedAsync();
            StatusText = $"✓ Heruntergeladen: {result.FileName} — bereit im Downloads-Tab.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download fehlgeschlagen für {title}", target.Title);
            StatusText = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
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
