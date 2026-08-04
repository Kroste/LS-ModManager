using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Kroste-Auto-Update: prüft GitHub-Releases von <c>github.com/Kroste/LS-ModManager</c>,
/// wählt das passende Release-Asset für die laufende Plattform (Windows-ZIP /
/// Linux-AppImage / Linux-tar.gz), lädt es mit Fortschritt herunter und startet
/// einen plattformspezifischen Installer, der die App ersetzt und neu startet.
/// Volles Muster: `references/autoupdate.md`.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string Repo = "Kroste/LS-ModManager";
    private const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/" + Repo + "/releases/latest";
    private const string AppName = "LSModManager";

    private readonly HttpClient _http;
    private readonly Lazy<Version> _current;
    private GithubRelease? _lastRelease;

    public UpdateService()
    {
        // Proxy-aware für Arbeitslaptop (Sophos-Kerberos); auf Bazzite No-Op.
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        _http = new HttpClient(handler);
        // KEIN globales Timeout — Downloads sind >100 MB, ein Timeout auf dem
        // Handler killt den Stream mittendrin. Der Check hat eigenes Timeout.
        var version = CurrentVersionString;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{AppName}/{version} (+https://github.com/{Repo})");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _current = new Lazy<Version>(() =>
            Version.TryParse(CurrentVersionString.Split('-', '+')[0], out var v)
                ? v : new Version(0, 0, 0));
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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var json = await _http.GetStringAsync(ReleasesApi, timeoutCts.Token).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GithubRelease>(json);
            if (release?.TagName is null)
            {
                Log.Warn("GitHub-Release-Antwort ohne tag_name");
                return new UpdateCheckResult(false, null, false);
            }

            var tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag.Split('-', '+')[0], out var latest))
            {
                Log.Warn("Ungültige Version im Tag: {tag}", release.TagName);
                return new UpdateCheckResult(false, release.TagName, false);
            }

            _lastRelease = release;
            var available = latest > _current.Value;
            var installableHere = available && PickAssetForCurrentPlatform(release, tag) is not null;
            Log.Info("Update-Check: aktuell={cur} neuestes={latest} verfügbar={avail} installierbar={inst}",
                _current.Value, latest, available, installableHere);
            return new UpdateCheckResult(available, tag, installableHere);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn(ex, "Update-Check: Netzwerkfehler");
            return new UpdateCheckResult(false, null, false);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Check fehlgeschlagen");
            return new UpdateCheckResult(false, null, false);
        }
    }

    /// <summary>
    /// Lädt das passende Release-Asset herunter und startet den plattform-
    /// spezifischen Installer. Kehrt in der Regel NICHT zurück — die App wird
    /// beendet, damit der Installer die Dateien austauschen kann. Kein passendes
    /// Asset (fremde Plattform/Arch) → wirft <see cref="InvalidOperationException"/>.
    /// </summary>
    public async Task DownloadAndInstallAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        if (_lastRelease is null)
        {
            await CheckForUpdateAsync(ct);
            if (_lastRelease is null) throw new InvalidOperationException("Kein Release-Info verfügbar");
        }
        var release = _lastRelease;
        var tag = release.TagName!.TrimStart('v', 'V');
        var asset = PickAssetForCurrentPlatform(release, tag)
            ?? throw new InvalidOperationException(
                "Kein passendes Release-Asset für diese Plattform gefunden");

        Log.Info("Update-Download: {name} ({url})", asset.Name, asset.DownloadUrl);
        var workDir = GetUpdateWorkDir();
        var downloadPath = Path.Combine(workDir, asset.Name!);

        // Download mit Progress (200 ms Takt für UI).
        using (var response = await _http.GetAsync(asset.DownloadUrl!,
                   HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = File.Create(downloadPath);
            var buffer = new byte[81_920];
            long done = 0;
            int read;
            var lastReport = DateTime.UtcNow;
            while ((read = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 200 && total is > 0)
                {
                    progress?.Report((double)done / total.Value);
                    lastReport = now;
                }
            }
            progress?.Report(1.0);
        }
        Log.Info("Update heruntergeladen: {p}", downloadPath);

        // Installer-Skript schreiben + starten, App beenden.
        LaunchInstaller(downloadPath, workDir);
    }

    private static string GetUpdateWorkDir()
    {
        // Bewusst NICHT AppContext.BaseDirectory — beim AppImage ist das der
        // read-only Squashfs-Mount. Wir nehmen ein user-writable Verzeichnis
        // (autoupdate.md Falle 2).
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName, "update");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
                xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "state");
            root = Path.Combine(xdg, AppName, "update");
        }
        Directory.CreateDirectory(root);
        return root;
    }

    private void LaunchInstaller(string downloadedAssetPath, string workDir)
    {
        var pid = Environment.ProcessId;
        // Environment.ProcessPath ist in normalem UND single-file-Publish zuverlässig.
        // Assembly.Location wäre der Fallback, ist aber in single-file-apps immer leer
        // (Compiler-Warning IL3000) — deswegen hart werfen wenn ProcessPath null ist.
        var appExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Konnte den eigenen Prozesspfad nicht ermitteln.");
        var installBase = OperatingSystem.IsWindows()
            ? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
        var logPath = Path.Combine(workDir, "update.log");

        string scriptPath;
        string command;
        string args;

        if (OperatingSystem.IsWindows())
        {
            scriptPath = Path.Combine(workDir, "install.bat");
            WriteWindowsInstaller(scriptPath, pid, downloadedAssetPath, installBase, appExe, logPath);
            command = "cmd.exe";
            args = $"/C \"{scriptPath}\"";
        }
        else if (IsAppImage(out var appImagePath))
        {
            scriptPath = Path.Combine(workDir, "install-appimage.sh");
            WriteAppImageInstaller(scriptPath, pid, downloadedAssetPath, appImagePath!, logPath);
            command = "/bin/bash";
            args = scriptPath;
        }
        else
        {
            scriptPath = Path.Combine(workDir, "install-tarball.sh");
            WriteTarballInstaller(scriptPath, pid, downloadedAssetPath, installBase, appExe, logPath);
            command = "/bin/bash";
            args = scriptPath;
        }

        Log.Info("Installer startet: {c} {a} (log: {l})", command, args, logPath);
        Process.Start(new ProcessStartInfo(command, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        // Kill-Fallback: wenn Environment.Exit im UI-Thread hängt, killen wir
        // uns nach 1,5s selbst — der Installer wartet nur auf kill -0 / pid.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try { Process.GetCurrentProcess().Kill(); } catch { /* App längst weg */ }
        });
        Environment.Exit(0);
    }

    private static bool IsAppImage(out string? appImagePath)
    {
        appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        return !string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath);
    }

    // --- Installer-Skripte (bewusst als plain-text, Zeilen ohne Einrückung) ---

    private static void WriteWindowsInstaller(string path, int pid, string zipPath,
        string installDir, string appExe, string logPath)
    {
        // Batch-Falle: Zeilen dürfen NICHT eingerückt sein (`:label` bricht sonst).
        // File.WriteAllLines mit sauberen Zeilen, kein Raw-String mit Indentation.
        var lines = new[]
        {
            "@echo off",
            $"echo Warte auf App-Ende (PID {pid}) > \"{logPath}\"",
            $"powershell -NoProfile -Command \"Wait-Process -Id {pid} -ErrorAction SilentlyContinue\"",
            "timeout /t 1 /nobreak > nul",
            $"echo Entpacke {zipPath} >> \"{logPath}\"",
            $"powershell -NoProfile -Command \"Expand-Archive -Force -Path '{zipPath}' -DestinationPath '{Path.GetDirectoryName(zipPath)}\\extracted'\"",
            $"echo Kopiere >> \"{logPath}\"",
            $"xcopy /Y /E /I \"{Path.GetDirectoryName(zipPath)}\\extracted\\*\" \"{installDir.TrimEnd('\\')}\" >> \"{logPath}\" 2>&1",
            $"echo Starte >> \"{logPath}\"",
            $"start \"\" \"{appExe}\"",
        };
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteAppImageInstaller(string path, int pid, string newAppImage,
        string runningAppImage, string logPath)
    {
        // FALLE: cp/mv über die laufende AppImage schlägt fehl ("Text file busy")
        // solange sie als Loop-Device gemountet ist → cp -f überschreibt Inode-
        // stabil. Neustart via setsid, damit der Kindprozess das sterbende Skript
        // überlebt. Log NICHT nach AppContext.BaseDirectory (read-only Squashfs!).
        var sb = new StringBuilder();
        sb.Append("#!/bin/bash\n");
        sb.Append($"exec >> \"{logPath}\" 2>&1\n");
        sb.Append($"echo \"Warte auf PID {pid}\"\n");
        sb.Append($"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done\n");
        sb.Append("sleep 1\n");
        sb.Append($"echo \"Kopiere {newAppImage} → {runningAppImage}\"\n");
        sb.Append($"cp -f \"{newAppImage}\" \"{runningAppImage}\"\n");
        sb.Append($"chmod +x \"{runningAppImage}\"\n");
        sb.Append("echo \"Starte neu\"\n");
        sb.Append($"setsid \"{runningAppImage}\" >/dev/null 2>&1 < /dev/null &\n");
        File.WriteAllText(path, sb.ToString());
        MakeExecutable(path);
    }

    private static void WriteTarballInstaller(string path, int pid, string tarPath,
        string installDir, string appExe, string logPath)
    {
        var sb = new StringBuilder();
        sb.Append("#!/bin/bash\n");
        sb.Append($"exec >> \"{logPath}\" 2>&1\n");
        sb.Append($"echo \"Warte auf PID {pid}\"\n");
        sb.Append($"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done\n");
        sb.Append("sleep 1\n");
        sb.Append($"echo \"Entpacke {tarPath} → {installDir}\"\n");
        sb.Append($"tar -xzf \"{tarPath}\" -C \"{installDir}\"\n");
        sb.Append($"chmod +x \"{appExe}\"\n");
        sb.Append($"setsid \"{appExe}\" >/dev/null 2>&1 < /dev/null &\n");
        File.WriteAllText(path, sb.ToString());
        MakeExecutable(path);
    }

    private static void MakeExecutable(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "chmod +x fehlgeschlagen: {p}", path);
        }
    }

    /// <summary>
    /// Wählt das passende Release-Asset für die laufende Plattform. Namensschema
    /// aus <c>.github/workflows/release.yml</c>:
    /// <list type="bullet">
    ///   <item>Windows: <c>LSModManager-X.Y.Z-win-x64.zip</c></item>
    ///   <item>Linux AppImage: <c>LSModManager-X.Y.Z-x86_64.AppImage</c></item>
    ///   <item>Linux tar.gz: <c>LSModManager-X.Y.Z-linux-x64.tar.gz</c></item>
    /// </list>
    /// </summary>
    public static GithubAsset? PickAssetForCurrentPlatform(GithubRelease release, string version)
    {
        if (release.Assets is null || release.Assets.Count == 0) return null;
        var assets = release.Assets;
        GithubAsset? Pick(string suffix) => assets.FirstOrDefault(a =>
            (a.Name ?? "").EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (OperatingSystem.IsWindows()) return Pick("-win-x64.zip");
        if (OperatingSystem.IsLinux())
        {
            // AppImage bevorzugt, wenn wir aus einem AppImage laufen — sonst tarball.
            if (IsAppImage(out _))
            {
                var img = Pick("-x86_64.AppImage");
                if (img is not null) return img;
            }
            return Pick("-linux-x64.tar.gz") ?? Pick("-x86_64.AppImage");
        }
        return null;
    }

    public void Dispose() => _http.Dispose();

    public sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public List<GithubAsset>? Assets { get; set; }
    }

    public sealed class GithubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion, bool InstallableHere);
