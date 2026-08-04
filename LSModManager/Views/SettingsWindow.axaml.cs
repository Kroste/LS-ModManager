using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LSModManager.ViewModels;
using NLog;

namespace LSModManager.Views;

public partial class SettingsWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsWindowViewModel vm) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Mod-Ordner auswählen",
                AllowMultiple = false,
            });
            var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
                vm.ApplyPickedPath(path);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Folder-Picker fehlgeschlagen");
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SaveSettingsCommand.Execute(null);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
