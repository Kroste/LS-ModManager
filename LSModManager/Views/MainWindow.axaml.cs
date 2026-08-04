using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
    }

    private void OnCatalogDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedCatalog is null) return;
        vm.ShowDetailsCommand.Execute(vm.SelectedCatalog);
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
}
