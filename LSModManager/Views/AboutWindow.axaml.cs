using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LSModManager.Services;
using NLog;

namespace LSModManager.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/Kroste/LS-ModManager";
    private const string BmcUrl = "https://buymeacoffee.com/kroste";
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly UpdateService? _updateService;

    // Parameterloser Ctor für den XAML-Designer.
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(UpdateService updateService) : this()
    {
        _updateService = updateService;
        VersionText.Text = $"Version {updateService.CurrentVersion}";
        UpdateButton.Click += OnCheckUpdate;
        InstallUpdateButton.Click += OnInstallUpdate;
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        UpdateResult.Text = "Prüfe …";
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.UpdateAvailable)
            {
                UpdateResult.Text = $"Version {result.LatestVersion} verfügbar!";
                if (result.InstallableHere)
                {
                    InstallUpdateButton.IsVisible = true;
                    InstallUpdateButton.Content = $"⬇ Update auf v{result.LatestVersion} installieren";
                }
                else
                {
                    UpdateResult.Text += " (kein passendes Asset für diese Plattform — auf GitHub laden)";
                }
            }
            else
            {
                UpdateResult.Text = result.LatestVersion is null
                    ? "Kein Zugriff auf GitHub."
                    : "Du hast die aktuelle Version.";
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Prüfung im Über-Fenster fehlgeschlagen");
            UpdateResult.Text = "Prüfung fehlgeschlagen.";
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        UpdateResult.Text = "Lade Update …";
        try
        {
            var progress = new Progress<double>(f =>
            {
                UpdateProgress.Value = f;
                UpdateResult.Text = $"Lade Update … {f * 100:F0}%";
            });
            await _updateService.DownloadAndInstallAsync(progress);
            // Kehrt normalerweise nicht zurück — die App beendet sich selbst.
            UpdateResult.Text = "Installer läuft, App beendet sich …";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Installation fehlgeschlagen");
            UpdateResult.Text = $"Fehler: {ex.Message}";
            UpdateProgress.IsVisible = false;
            UpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void Launch(string url)
    {
        try
        {
            TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Link konnte nicht geöffnet werden: {url}", url);
        }
    }
}
