using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Katalog-Client für <c>modhoster.de</c> (zweite Mod-Quelle neben dem
/// GIANTS ModHub). Nutzt den offiziellen JSON-Endpunkt
/// <c>/mods.json?game_id=1</c> (game_id=1 ist Landwirtschafts Simulator 25).
/// <para>
/// <b>Kein In-App-Download</b> — modhoster verlangt Login-Session für die
/// eigentliche ZIP, und die robots.txt sperrt <c>/external/</c>, <c>/redirect/</c>
/// und <c>/login</c> explizit. Der Nutzer klickt in der App auf „🌐 Öffnen"
/// und macht den Download im Browser.
/// </para>
/// </summary>
public sealed class ModhosterCatalogService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string BaseUrl = "https://www.modhoster.de";
    // game_id=1 ist bei modhoster explizit „Landwirtschafts Simulator 25"
    // (das game_name-Feld im JSON bestätigt es).
    private const int Ls25GameId = 1;

    private readonly HttpClient _http;

    public ModhosterCatalogService()
    {
        _http = new HttpClient();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        // Transparenter User-Agent (keine Browser-Masquerade). Kein Referer nötig —
        // die JSON-API ist öffentlich, kein CDN-Referer-Check.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"LSModManager/{version} (+https://github.com/Kroste/LS-ModManager)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<IReadOnlyList<ModHubEntry>> FetchCatalogPageAsync(
        int page, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/mods.json?game_id={Ls25GameId}&page={page}";
        Log.Info("Modhoster-Katalog laden: {url}", url);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var json = await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return ParseCatalogJson(json);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Modhoster-Fetch fehlgeschlagen: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
    }

    /// <summary>Testbar: JSON → ModHubEntry-Liste.</summary>
    public static IReadOnlyList<ModHubEntry> ParseCatalogJson(string json)
    {
        var doc = JsonSerializer.Deserialize<ModhosterResponse>(json);
        if (doc?.Modifications is null || doc.Modifications.Count == 0)
            return Array.Empty<ModHubEntry>();

        var result = new List<ModHubEntry>(doc.Modifications.Count);
        foreach (var m in doc.Modifications)
        {
            if (string.IsNullOrWhiteSpace(m.CachedSlug) || string.IsNullOrWhiteSpace(m.Name))
                continue;
            var detailUrl = $"{BaseUrl}/mods/{m.CachedSlug}";
            var previewUrl = m.Image?.Urls?.Shop
                          ?? m.Image?.Urls?.Thumb
                          ?? m.ThumbUrl
                          ?? m.ImageUrl
                          ?? "";
            result.Add(new ModHubEntry(
                Title: m.Name!,
                Author: m.User?.Name ?? "",
                Category: m.GameName ?? "",
                PreviewUrl: previewUrl,
                DetailUrl: detailUrl,
                Version: null,
                SizeText: null,
                Source: ModHubEntry.ModhosterSource,
                CanInAppDownload: false));
        }
        return result;
    }

    public void Dispose() => _http.Dispose();

    // --- JSON-DTOs (nur was wir wirklich brauchen) ---

    private sealed class ModhosterResponse
    {
        [JsonPropertyName("modifications")] public List<Modification>? Modifications { get; set; }
    }

    private sealed class Modification
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("cached_slug")] public string? CachedSlug { get; set; }
        [JsonPropertyName("game_name")] public string? GameName { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
        [JsonPropertyName("thumb_url")] public string? ThumbUrl { get; set; }
        [JsonPropertyName("image")] public ModImage? Image { get; set; }
        [JsonPropertyName("user")] public ModUser? User { get; set; }
    }

    private sealed class ModImage
    {
        [JsonPropertyName("urls")] public ImageUrls? Urls { get; set; }
    }

    private sealed class ImageUrls
    {
        [JsonPropertyName("shop")] public string? Shop { get; set; }
        [JsonPropertyName("thumb")] public string? Thumb { get; set; }
    }

    private sealed class ModUser
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
