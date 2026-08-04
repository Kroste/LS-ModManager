using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Kroste-Auto-Update-Check (proxy-aware, nicht blockierend): prüft GitHub-Releases
/// von <c>github.com/Kroste/LS-ModManager</c> und meldet, ob eine neuere Version
/// verfügbar ist. Der eigentliche Self-Update-Download ist Phase 2 (autoupdate.md
/// vollständig ist ~200 LoC — wir liefern hier den Check und öffnen die Release-
/// Seite im Browser, das reicht für v0.1.0).
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string ReleasesApi =
        "https://api.github.com/repos/Kroste/LS-ModManager/releases/latest";
    public const string ReleasesPageUrl =
        "https://github.com/Kroste/LS-ModManager/releases/latest";

    private readonly HttpClient _http;
    private readonly Lazy<Version> _current;

    public UpdateService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var version = CurrentVersionString;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"LSModManager/{version} (+https://github.com/Kroste/LS-ModManager)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _current = new Lazy<Version>(() =>
            Version.TryParse(CurrentVersionString.Split('-', '+')[0], out var v) ? v : new Version(0, 0, 0));
    }

    public string CurrentVersion => CurrentVersionString;

    private static string CurrentVersionString =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(ReleasesApi, ct).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GithubRelease>(json);
            if (release?.TagName is null)
            {
                Log.Warn("GitHub-Release-Antwort ohne tag_name");
                return new UpdateCheckResult(false, null);
            }

            var tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag.Split('-', '+')[0], out var latest))
            {
                Log.Warn("Ungültige Version im Tag: {tag}", release.TagName);
                return new UpdateCheckResult(false, release.TagName);
            }

            var available = latest > _current.Value;
            Log.Info("Update-Check: aktuell={cur} neuestes={latest} verfügbar={avail}",
                _current.Value, latest, available);
            return new UpdateCheckResult(available, tag);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn(ex, "Update-Check: Netzwerkfehler");
            return new UpdateCheckResult(false, null);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Check fehlgeschlagen");
            return new UpdateCheckResult(false, null);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion);
