using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LSModManager.Services.Ai;
using LSModManager.ViewModels;
using NLog;

namespace LSModManager.Views;

public partial class SettingsWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public SettingsWindow()
    {
        InitializeComponent();
        // VM-Event → tatsächliches Fenster (VM darf keine Views instanziieren).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsWindowViewModel vm)
                vm.OllamaPullRequested += OnOllamaPullRequested;
        };
    }

    private async void OnOllamaPullRequested(OllamaProvider provider, string modelName)
    {
        try
        {
            var pullVm = new OllamaPullViewModel(provider, modelName);
            var window = new OllamaPullWindow { DataContext = pullVm };
            await window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ollama-Pull-Fenster konnte nicht geöffnet werden");
        }
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
