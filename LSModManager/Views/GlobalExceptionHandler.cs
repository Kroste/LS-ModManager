using NLog;

namespace LSModManager.Views;

/// <summary>
/// Fängt unbehandelte Exceptions global ab (AppDomain + TaskScheduler),
/// loggt sie als Fatal und verhindert stillen Absturz.
/// </summary>
internal static class GlobalExceptionHandler
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log.Fatal(ex, "Unbehandelte AppDomain-Exception (IsTerminating={term})", e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Fatal(e.Exception, "Unbeobachtete Task-Exception");
            e.SetObserved();
        };
    }
}
