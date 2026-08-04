using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NLog;

namespace LSModManager.Views;

/// <summary>
/// Kroste-Tray: Minimieren → Hide, Schließen → Beenden. Klick aufs Icon = Restore.
/// Vier Pflicht-Absicherungen: GC-Referenz (Feld in App), Restore-Guard-Flag,
/// try/catch mit Fallback, Icon-Load defensiv.
/// </summary>
public sealed class TrayController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Application _app;
    private readonly Window _window;
    private TrayIcon? _tray;
    private bool _restoreInProgress;

    public TrayController(Application app, Window window)
    {
        _app = app;
        _window = window;
    }

    public void Install()
    {
        try
        {
            var iconUri = new Uri("avares://LSModManager/Assets/lsmodmanager.png");
            var icon = AssetLoader.Exists(iconUri)
                ? new WindowIcon(new Bitmap(AssetLoader.Open(iconUri)))
                : null;

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "LS-ModManager",
                IsVisible = true,
                Menu = BuildMenu(),
            };
            _tray.Clicked += (_, _) => Restore();

            TrayIcon.SetIcons(_app, new TrayIcons { _tray });
            _window.PropertyChanged += OnWindowPropertyChanged;

            Log.Info("System-Tray installiert (Minimize → Tray).");
        }
        catch (Exception ex)
        {
            _tray = null;
            Log.Warn(ex, "System-Tray nicht verfügbar — Fallback: Standard-Minimieren.");
        }
    }

    public void Restore()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _restoreInProgress = true;
            try
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
            finally
            {
                _restoreInProgress = false;
            }
        });
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var show = new NativeMenuItem("Anzeigen");
        show.Click += (_, _) => Restore();
        menu.Add(show);

        menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Beenden");
        quit.Click += (_, _) => Quit();
        menu.Add(quit);
        return menu;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty) return;
        if (_restoreInProgress) return;
        if (_window.WindowState != WindowState.Minimized) return;
        _window.Hide();
    }

    private void Quit()
    {
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
