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
