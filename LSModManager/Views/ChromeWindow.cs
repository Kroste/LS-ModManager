using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NLog;

namespace LSModManager.Views;

/// <summary>
/// Custom-Chrome nach Kroste-Standard (Avalonia 12): BorderOnly (NICHT None — sonst
/// fehlen die nativen Resize-Griffe) und Client-Area bis in die Dekoration ausgedehnt.
/// Ohne ExtendClientArea liegt die OS-Caption-Hit-Test-Zone über der eigenen
/// Titelleiste und schluckt Klicks und Drag!
/// </summary>
public class ChromeWindow : Window
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;

        try
        {
            var uri = new Uri("avares://LSModManager/Assets/lsmodmanager.png");
            if (AssetLoader.Exists(uri))
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(uri)));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Fenster-Icon konnte nicht geladen werden — weiter ohne");
        }
    }
}
