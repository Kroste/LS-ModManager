using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LSModManager.Localization;
using LSModManager.Services;
using LSModManager.ViewModels;
using LSModManager.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace LSModManager;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public IServiceProvider Services { get; private set; } = null!;

    // GC-Referenz halten — sonst sammelt der Collector das Tray-Icon ein (Kroste-Falle).
    private TrayController? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();

        // Globaler Exception-Handler VOR dem ersten Fenster registrieren.
        GlobalExceptionHandler.Install();

        // UI-Sprache VOR dem ersten Fenster-Bau setzen — sonst rendert das
        // MainWindow kurz in einer anderen Sprache und flackert beim späteren
        // Wechsel. Bei null (Erstinstallation) bleibt die OS-Kultur.
        var storedCulture = Services.GetRequiredService<AppSettingsService>().Current.UiCulture;
        if (!string.IsNullOrWhiteSpace(storedCulture))
            LocalizationService.Instance.SetCulture(storedCulture);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var main = new MainWindow { DataContext = mainVm };
            desktop.MainWindow = main;

            // Tray NACH MainWindow bauen und als Feld halten (GC-Falle).
            _tray = new TrayController(this, main);
            _tray.Install();

            // Beim regulären Shutdown Settings speichern (Persistenz-Kroste-Regel).
            desktop.Exit += (_, _) =>
            {
                try { Services.GetRequiredService<AppSettingsService>().Save(); }
                catch (Exception ex) { Log.Warn(ex, "Konnte Settings beim Exit nicht speichern"); }
            };

            Log.Info("MainWindow angezeigt, Tray installiert.");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Services (Singletons)
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<ModPathService>();
        services.AddSingleton<ModDescReader>();
        services.AddSingleton<ModInstallService>();
        services.AddSingleton<ModBackupService>();
        services.AddSingleton<ModHubService>();

        // KI-Integration (Kroste-Baukasten): AiSettingsService hält die
        // persistente Provider-Config (Ollama/Anthropic/OpenAI/Gemini/Mistral/
        // OpenAI-kompatibel); AiProviderFactory bildet daraus zur Laufzeit den
        // passenden IAiProvider. AddHttpClient() registriert die IHttpClientFactory,
        // die die Provider intern für ihre Requests nutzen (Kanal „ai" für
        // Completions, „ai-pull" für Ollama-Modell-Downloads).
        services.AddSingleton<Services.Ai.AiSettingsService>();
        services.AddSingleton<Services.Ai.AiProviderFactory>();
        services.AddHttpClient();
        services.AddSingleton<ModhosterCatalogService>();
        services.AddSingleton<HofHirschfeldCatalogService>();
        services.AddSingleton<UpdateService>();

        // ViewModels (transient — jedes Fenster bekommt seins)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
