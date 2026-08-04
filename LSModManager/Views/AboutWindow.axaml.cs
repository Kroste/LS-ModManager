using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LSModManager.Localization;
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
        VersionText.Text = L.T("About_VersionPrefix") + updateService.CurrentVersion;
        UpdateButton.Click += OnCheckUpdate;
        InstallUpdateButton.Click += OnInstallUpdate;
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
        LogFolderButton.Click += (_, _) => OpenLogFolder();
    }

    /// <summary>
    /// Öffnet den Ordner mit den NLog-Log-Dateien im System-Dateimanager.
    /// Pfad ist relativ zur nlog.config (<c>logs/</c> neben der Exe) —
    /// mit <see cref="AppContext.BaseDirectory"/> aufgelöst. Falls der
    /// Ordner noch nicht existiert (frisch installierte App vor dem ersten
    /// Log-Write), wird er angelegt, damit der Dateimanager nicht ins
    /// Leere zeigt.
    /// </summary>
    private void OpenLogFolder()
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Log-Ordner konnte nicht geöffnet werden");
        }
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        UpdateResult.Text = L.T("About_Checking");
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.UpdateAvailable)
            {
                UpdateResult.Text = L.T("About_VersionAvailablePrefix")
                    + result.LatestVersion
                    + L.T("About_VersionAvailableSuffix");
                if (result.InstallableHere)
                {
                    InstallUpdateButton.IsVisible = true;
                    InstallUpdateButton.Content = L.T("About_InstallUpdatePrefix") + result.LatestVersion;
                }
                else
                {
                    UpdateResult.Text += L.T("About_NoAssetForPlatform");
                }
            }
            else
            {
                UpdateResult.Text = result.LatestVersion is null
                    ? L.T("About_NoAccess")
                    : L.T("About_UpToDate");
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Prüfung im Über-Fenster fehlgeschlagen");
            UpdateResult.Text = L.T("About_CheckFailed");
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
        UpdateResult.Text = L.T("About_Downloading");
        try
        {
            var progress = new Progress<double>(f =>
            {
                UpdateProgress.Value = f;
                UpdateResult.Text = L.T("About_DownloadingPrefix") + $"{f * 100:F0}%";
            });
            await _updateService.DownloadAndInstallAsync(progress);
            // Kehrt normalerweise nicht zurück — die App beendet sich selbst.
            UpdateResult.Text = L.T("About_InstallerRunning");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Installation fehlgeschlagen");
            UpdateResult.Text = L.T("About_ErrorPrefix") + ex.Message;
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
