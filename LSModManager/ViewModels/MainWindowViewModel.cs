using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly AppSettingsService _settings;
    private readonly UpdateService _updates;

    public MainWindowViewModel(
        ModInstallService install,
        ModPathService paths,
        ModHubService hub,
        AppSettingsService settings,
        UpdateService updates)
    {
        _install = install;
        _paths = paths;
        _hub = hub;
        _settings = settings;
        _updates = updates;

        ModPath = _paths.GetModPath() ?? "";
        _ = RefreshInstalledAsync();
    }

    public ObservableCollection<InstalledModItemViewModel> InstalledMods { get; } = new();
    public ObservableCollection<ModHubItemViewModel> CatalogMods { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEnabledCommand))]
    private InstalledModItemViewModel? _selectedInstalled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenModHubDetailCommand))]
    private ModHubItemViewModel? _selectedCatalog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenModFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallFromZipCommand))]
    private string _modPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInstalledCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCatalogCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallFromZipCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEnabledCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    public string CurrentVersionText => $"v{_updates.CurrentVersion}";

    // ---- Commands ----

    [RelayCommand(CanExecute = nameof(CanRefreshInstalled))]
    public async Task RefreshInstalledAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Lade installierte Mods …";
            var list = await Task.Run(() => _install.ListInstalled());
            InstalledMods.Clear();
            foreach (var m in list) InstalledMods.Add(new InstalledModItemViewModel(m));
            StatusText = $"{InstalledMods.Count} Mods gefunden.";
            Log.Info("Installierte Mods aktualisiert: {n}", InstalledMods.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh installierter Mods fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanRefreshInstalled() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefreshCatalog))]
    public async Task RefreshCatalogAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Lade ModHub-Katalog …";
            var lang = _settings.Current.CatalogLanguage ?? "de";
            var entries = await _hub.FetchCatalogPageAsync(1, lang);
            CatalogMods.Clear();
            foreach (var e in entries) CatalogMods.Add(new ModHubItemViewModel(e));
            StatusText = entries.Count > 0
                ? $"Katalog geladen: {entries.Count} Einträge."
                : "Katalog leer oder nicht erreichbar (siehe Log).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Katalog-Refresh fehlgeschlagen");
            StatusText = $"Katalog-Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanRefreshCatalog() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstallFromZip))]
    public async Task InstallFromZipAsync(string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath)) return;
        try
        {
            IsBusy = true;
            StatusText = $"Installiere {System.IO.Path.GetFileName(zipPath)} …";
            var overwriteMode = true; // Nutzer hat im Dialog bewusst gewählt
            await Task.Run(() => _install.Install(zipPath!, overwrite: overwriteMode));
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

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    public async Task UninstallAsync()
    {
        var sel = SelectedInstalled;
        if (sel is null) return;
        try
        {
            IsBusy = true;
            StatusText = $"Deinstalliere {sel.DisplayTitle} …";
            await Task.Run(() => _install.Uninstall(sel.Model));
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

    private bool CanUninstall() => !IsBusy && SelectedInstalled is not null;

    [RelayCommand(CanExecute = nameof(CanToggleEnabled))]
    public async Task ToggleEnabledAsync()
    {
        var sel = SelectedInstalled;
        if (sel is null) return;
        try
        {
            IsBusy = true;
            var wantEnabled = !sel.Model.IsEnabled;
            await Task.Run(() => _install.SetEnabled(sel.Model, wantEnabled));
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

    private bool CanToggleEnabled() => !IsBusy && SelectedInstalled is not null;

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    public void OpenModFolder()
    {
        try
        {
            var path = _paths.GetModPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Mod-Ordner nicht öffnen");
            StatusText = $"Fehler: {ex.Message}";
        }
    }

    private bool CanOpenModFolder() => !string.IsNullOrWhiteSpace(ModPath);

    [RelayCommand(CanExecute = nameof(CanOpenModHubDetail))]
    public void OpenModHubDetail()
    {
        var sel = SelectedCatalog;
        if (sel is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(sel.DetailUrl) { UseShellExecute = true });
            StatusText = $"Browser geöffnet: {sel.Title}";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Browser nicht öffnen");
            StatusText = $"Fehler: {ex.Message}";
        }
    }

    private bool CanOpenModHubDetail() => SelectedCatalog is not null;

    /// <summary>Nach Änderungen von ModPath (z.B. aus Settings): Anzeige aktualisieren.</summary>
    public void ReloadPath()
    {
        ModPath = _paths.GetModPath() ?? "";
        _ = RefreshInstalledAsync();
    }
}
