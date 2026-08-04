using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LSModManager.Services;
using NLog;

namespace LSModManager.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsService _settings;
    private readonly ModPathService _paths;

    public SettingsWindowViewModel(AppSettingsService settings, ModPathService paths)
    {
        _settings = settings;
        _paths = paths;

        ModPathOverride = _settings.Current.ModPathOverride ?? "";
        DetectedPath = _paths.DetectModPath() ?? "(nicht gefunden)";
        CatalogLanguage = _settings.Current.CatalogLanguage ?? "de";
    }

    public event EventHandler? SettingsChanged;

    [ObservableProperty] private string _modPathOverride = "";
    [ObservableProperty] private string _detectedPath = "";
    [ObservableProperty] private string _catalogLanguage = "de";

    [RelayCommand]
    public void Detect()
    {
        DetectedPath = _paths.DetectModPath() ?? "(nicht gefunden)";
        Log.Info("Manuelle Pfad-Detection: {p}", DetectedPath);
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _settings.Update(s =>
        {
            s.ModPathOverride = string.IsNullOrWhiteSpace(ModPathOverride) ? null : ModPathOverride.Trim();
            s.CatalogLanguage = CatalogLanguage;
        });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        Log.Info("Settings gespeichert: override={o} lang={l}",
            _settings.Current.ModPathOverride ?? "<null>", _settings.Current.CatalogLanguage);
    }

    public void ApplyPickedPath(string path)
    {
        ModPathOverride = path;
    }
}
