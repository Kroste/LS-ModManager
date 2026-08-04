using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using LSModManager.Localization;
using LSModManager.Services;
using LSModManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace LSModManager.Views;

public partial class MainWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public MainWindow()
    {
        InitializeComponent();
        // Detail-Request vom ViewModel in ein ChromeWindow verwandeln — der VM darf
        // keine Views instanziieren, deswegen der Umweg über ein Event.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.DetailRequested += OnDetailRequested;
        };
        // Drag-and-Drop von Mod-ZIPs auf's ganze Fenster.
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Keyboard-Shortcuts (Handler unten).
        KeyDown += OnMainKeyDown;
    }

    private async void OnMainKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Ctrl+F: Fokus auf's Installiert-Suchfeld. TextBox darf auch aktiv sein —
        // wir überschreiben deren Text nicht, sondern selektieren nur.
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            InstalledSearchBox?.Focus();
            InstalledSearchBox?.SelectAll();
            e.Handled = true;
            return;
        }

        // F5: Installierte Mods neu laden. Wenn ein TextBox den Fokus hat, ist
        // F5 dort nicht belegt — also frei nutzbar.
        if (e.Key == Key.F5)
        {
            if (vm.RefreshInstalledCommand.CanExecute(null))
                vm.RefreshInstalledCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Delete: markierte Mods deinstallieren, mit Bestätigungsdialog.
        // Nur greifen wenn der Focus auf der ListBox liegt — sonst wäre das im
        // Suchfeld ein zerstörerischer Fehltritt (User will „Zeichen löschen").
        if (e.Key == Key.Delete && FocusManager?.GetFocusedElement() is Control focused &&
            (focused == InstalledList || focused.FindLogicalAncestorOfType<ListBox>() == InstalledList))
        {
            var items = InstalledList?.SelectedItems?.OfType<InstalledModItemViewModel>().ToList();
            if (items is null || items.Count == 0) return;
            var confirmed = await ConfirmDialog.ShowAsync(this,
                L.T("Confirm_BulkUninstall_Title"),
                L.F("Confirm_BulkUninstall_Message", items.Count));
            if (confirmed) await vm.BulkUninstallAsync(items);
            e.Handled = true;
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        try
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is null) return;
            var paths = files
                .OfType<IStorageFile>()
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) return;
            await vm.InstallZipsAsync(paths);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Drag-and-Drop-Install fehlgeschlagen");
        }
    }

    private void OnCatalogDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedCatalog is null) return;
        vm.ShowDetailsCommand.Execute(vm.SelectedCatalog);
    }

    private void OnInstalledDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        // Nur wenn ein Katalog-Match existiert — sonst bleibt es still (der
        // Nutzer merkt: „aha, für den Mod ist kein Detail verfügbar").
        vm.TryShowInstalledDetails(vm.SelectedInstalled);
    }

    private void OnDetailRequested(ModHubItemViewModel item)
    {
        try
        {
            var modId = ExtractModId(item.DetailUrl);
            if (modId is null)
            {
                Log.Warn("Detail-Request ohne parsbare mod_id: {url}", item.DetailUrl);
                return;
            }
            if (App.Current is not App app || DataContext is not MainWindowViewModel main) return;
            var hub = app.Services.GetRequiredService<ModHubService>();
            var settings = app.Services.GetRequiredService<AppSettingsService>();
            var detailVm = new ModDetailViewModel(hub, settings, main, modId.Value, item.Title);
            var window = new ModDetailWindow(detailVm);
            _ = window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Detail-Window konnte nicht geöffnet werden");
        }
    }

    private static int? ExtractModId(string url)
    {
        var m = Regex.Match(url, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private async void OnInstallZipClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Mod-ZIP auswählen",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Mod-Archiv (*.zip)") { Patterns = new[] { "*.zip" } },
                    FilePickerFileTypes.All,
                },
            });
            var picked = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(picked)) return;
            await vm.InstallFromZipAsync(picked);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "File-Picker fehlgeschlagen");
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (App.Current is not App app) return;
        var settingsVm = app.Services.GetRequiredService<SettingsWindowViewModel>();
        settingsVm.SettingsChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.ReloadPath();
        };
        var window = new SettingsWindow { DataContext = settingsVm };
        _ = window.ShowDialog(this);
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (App.Current is not App app) return;
        var updates = app.Services.GetRequiredService<UpdateService>();
        var window = new AboutWindow(updates);
        _ = window.ShowDialog(this);
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var suggestedName = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                L.T("Backup_DefaultFileName"),
                DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Backup_SaveDialogTitle"),
                SuggestedFileName = suggestedName,
                DefaultExtension = "zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(L.T("Backup_FileTypeLabel")) { Patterns = new[] { "*.zip" } },
                },
            });
            var picked = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(picked)) return;
            await vm.CreateBackupAsync(picked);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup-Dialog fehlgeschlagen");
        }
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("Restore_OpenDialogTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(L.T("Backup_FileTypeLabel")) { Patterns = new[] { "*.zip" } },
                    FilePickerFileTypes.All,
                },
            });
            var picked = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(picked)) return;
            await vm.RestoreBackupAsync(picked);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore-Dialog fehlgeschlagen");
        }
    }

    private async void OnBulkEnableClick(object? sender, RoutedEventArgs e) =>
        await RunBulk(items => (DataContext as MainWindowViewModel)!.BulkSetEnabledAsync(items, true));

    private async void OnBulkDisableClick(object? sender, RoutedEventArgs e) =>
        await RunBulk(items => (DataContext as MainWindowViewModel)!.BulkSetEnabledAsync(items, false));

    private async void OnBulkUninstallClick(object? sender, RoutedEventArgs e)
    {
        // Zerstörerische Bulk-Aktion — Bestätigung einfordern, sonst ist ein
        // Fehlklick sofort ein Uninstall-Marathon ohne Rückweg.
        var items = InstalledList?.SelectedItems?.OfType<InstalledModItemViewModel>().ToList();
        if (items is null || items.Count == 0) return;
        var confirmed = await ConfirmDialog.ShowAsync(this,
            L.T("Confirm_BulkUninstall_Title"),
            L.F("Confirm_BulkUninstall_Message", items.Count));
        if (!confirmed) return;
        if (DataContext is MainWindowViewModel vm)
            await vm.BulkUninstallAsync(items);
    }

    private async Task RunBulk(Func<IReadOnlyList<InstalledModItemViewModel>, Task> action)
    {
        if (DataContext is not MainWindowViewModel) return;
        var list = InstalledList;
        if (list?.SelectedItems is null) return;
        var items = list.SelectedItems
            .OfType<InstalledModItemViewModel>()
            .ToList();
        if (items.Count == 0) return;
        await action(items);
    }

    // ---- Rechtsklick-Menü auf Installiert-Cards ----
    //
    // Die MenuItem.DataContext-Kette: MenuItem → ContextMenu → PlacementTarget
    // (das ist die Card-Border) → deren DataContext = InstalledModItemViewModel.
    // Wir ziehen den DataContext deshalb aus dem sender-MenuItem.DataContext
    // (Avalonia setzt ihn dort korrekt).

    private static InstalledModItemViewModel? ItemFromMenu(object? sender) =>
        (sender as Control)?.DataContext as InstalledModItemViewModel;

    private void OnContextDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TryShowInstalledDetails(ItemFromMenu(sender));
    }

    private void OnContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        var item = ItemFromMenu(sender);
        if (item is null) return;
        try
        {
            var folder = System.IO.Path.GetDirectoryName(item.Model.FilePath);
            if (string.IsNullOrEmpty(folder)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
                { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn(ex, "Konnte Mod-Ordner nicht öffnen"); }
    }

    private async void OnContextCopyFilename(object? sender, RoutedEventArgs e)
    {
        var item = ItemFromMenu(sender);
        if (item is null) return;
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null) await clip.SetTextAsync(item.Model.FileName);
        }
        catch (Exception ex) { Log.Warn(ex, "Clipboard-Kopie fehlgeschlagen"); }
    }

    private async void OnContextUninstall(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var item = ItemFromMenu(sender);
        if (item is null) return;
        // Einzel-Uninstall bekommt keinen Dialog — ist eine Kartenweise
        // gezielte Aktion, kein Bulk-Missklick-Risiko wie „Alle deinstallieren".
        await vm.UninstallAsync(item);
    }
}
