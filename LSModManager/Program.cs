using System.IO;
using Avalonia;
using Avalonia.Media;
using LSModManager.Logging;
using NLog;
using NLog.Config;

namespace LSModManager;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // NLog: masked-LayoutRenderer VOR dem ersten Logger-Aufruf registrieren.
        // Sonst wird ${masked:...} nicht aufgelöst und die Log-Zeile ist kaputt.
        LogManager.Setup().SetupExtensions(ext =>
            ext.RegisterLayoutRenderer<MaskingLayoutRenderer>("masked"));

        // Config aus nlog.config neben der Exe laden (via CopyToOutputDirectory).
        var configPath = Path.Combine(AppContext.BaseDirectory, "nlog.config");
        if (File.Exists(configPath))
            LogManager.Configuration = new XmlLoggingConfiguration(configPath);

        var log = LogManager.GetCurrentClassLogger();
        try
        {
            log.Info("LSModManager gestartet (Args: {count})", args.Length);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "Unerwarteter Fehler beim Start");
            return 1;
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var emojiFont = OperatingSystem.IsWindows() ? "Segoe UI Emoji"
            : OperatingSystem.IsMacOS() ? "Apple Color Emoji"
            : "Noto Color Emoji";

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "fonts:Inter#Inter",
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(emojiFont) }],
            })
            .LogToTrace();
    }
}
