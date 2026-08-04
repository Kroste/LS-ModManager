using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using LSModManager.Models;
using NLog;

namespace LSModManager.Services;

/// <summary>
/// Liest den offiziellen ModHub-Katalog (farming-simulator.com/mods.php) für FS25
/// per HTTPS und parst die Mod-Karten. Der eigentliche Download läuft NIE hier —
/// die UI öffnet die Detail-URL im Browser, der Nutzer klickt selbst „Download".
/// Das ist die einzige ToS-konforme Variante ohne Modhub-API.
/// </summary>
public sealed class ModHubService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string BaseUrl = "https://www.farming-simulator.com";
    private const string ListPath = "/mods.php";
    // GIANTS-Konvention: title=fs2025 selektiert die FS25-Kategorie.
    private const string GameTitleSlug = "fs2025";

    private readonly HttpClient _http;

    public ModHubService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"LSModManager/{version} (+https://github.com/Kroste/LS-ModManager)");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9,en;q=0.5");
    }

    /// <summary>
    /// Holt eine Seite des Katalogs. <paramref name="page"/> ist 1-basiert.
    /// Liefert bei Fehlern eine leere Liste — Fehler stehen im Log.
    /// </summary>
    public async Task<IReadOnlyList<ModHubEntry>> FetchCatalogPageAsync(
        int page = 1, string language = "de", CancellationToken ct = default)
    {
        var url = BuildListUrl(page, language);
        Log.Info("ModHub-Katalog laden: {url}", url);
        try
        {
            var html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            return ParseListPage(html);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn(ex, "ModHub-Fetch fehlgeschlagen: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
        catch (TaskCanceledException ex)
        {
            Log.Warn(ex, "ModHub-Fetch Timeout: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
    }

    /// <summary>URL, die der Nutzer im Browser öffnen soll, um herunterzuladen.</summary>
    public string BuildDetailUrl(int modId, string language = "de") =>
        $"{BaseUrl}/mod.php?lang={language}&country=de&mod_id={modId}&title={GameTitleSlug}";

    internal static string BuildListUrl(int page, string language)
    {
        // Beispiel: https://www.farming-simulator.com/mods.php?title=fs2025&lang=de&country=de&page=2
        var pageSuffix = page > 1 ? $"&page={page}" : "";
        return $"{BaseUrl}{ListPath}?lang={language}&country=de&title={GameTitleSlug}{pageSuffix}";
    }

    /// <summary>
    /// Öffentlich testbar (parser-only, keine Netzwerk-Calls). Sucht in der Katalog-HTML
    /// alle Anker mit <c>mod.php?mod_id=NNN</c> und extrahiert Titel/Autor/Kategorie/Preview.
    /// </summary>
    public static IReadOnlyList<ModHubEntry> ParseListPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var entries = new List<ModHubEntry>();
        var seen = new HashSet<int>();

        // Jeder Katalog-Eintrag ist ein Link zu mod.php?mod_id=XXXX.
        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, 'mod.php') and contains(@href, 'mod_id=')]")
                      ?? new HtmlNodeCollection(doc.DocumentNode);

        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", "");
            var modId = ExtractModId(href);
            if (modId is null) continue;
            if (!seen.Add(modId.Value)) continue;

            // Karten-Container: nächster Vorfahr, der eine gemeinsame Struktur trägt.
            var card = anchor.Ancestors().FirstOrDefault(a =>
                a.HasClass("mod-item") || a.HasClass("card") || a.Name == "li" || a.Name == "article");

            var title = ExtractTitle(anchor, card);
            var previewUrl = ExtractPreview(anchor, card);
            var author = ExtractField(card, "author") ?? "";
            var category = ExtractField(card, "category") ?? "";
            var version = ExtractField(card, "version");
            var size = ExtractField(card, "size");

            var detailUrl = new Uri(new Uri(BaseUrl), HttpUtility.HtmlDecode(href)).ToString();

            entries.Add(new ModHubEntry(
                Title: title,
                Author: author,
                Category: category,
                PreviewUrl: previewUrl,
                DetailUrl: detailUrl,
                Version: version,
                SizeText: size));
        }

        Log.Info("Katalog geparst: {n} Einträge", entries.Count);
        return entries;
    }

    private static int? ExtractModId(string href)
    {
        var m = Regex.Match(href, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private static string ExtractTitle(HtmlNode anchor, HtmlNode? card)
    {
        // Bevorzugt: erster Text-Knoten mit sichtbarem Text im Anker
        var text = anchor.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(text) && text.Length < 200)
            return HttpUtility.HtmlDecode(text);

        // Alternative: title-Attribut oder alt-Text auf einem Bild
        var img = (anchor.SelectSingleNode(".//img") ?? card?.SelectSingleNode(".//img"))?
            .GetAttributeValue("alt", "");
        if (!string.IsNullOrWhiteSpace(img))
            return HttpUtility.HtmlDecode(img);

        var titleAttr = anchor.GetAttributeValue("title", "");
        return HttpUtility.HtmlDecode(titleAttr ?? "").Trim();
    }

    private static string ExtractPreview(HtmlNode anchor, HtmlNode? card)
    {
        var img = anchor.SelectSingleNode(".//img") ?? card?.SelectSingleNode(".//img");
        if (img is null) return "";
        var src = img.GetAttributeValue("data-src", "");
        if (string.IsNullOrWhiteSpace(src))
            src = img.GetAttributeValue("src", "");
        if (string.IsNullOrWhiteSpace(src)) return "";
        return src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? src
            : new Uri(new Uri(BaseUrl), src).ToString();
    }

    /// <summary>
    /// Sucht in der Card nach einem Feld, dessen CSS-Klasse den Namen enthält.
    /// Toleriert Struktur-Änderungen — findet er nichts, liefert er null.
    /// </summary>
    private static string? ExtractField(HtmlNode? card, string fieldName)
    {
        if (card is null) return null;
        var node = card.SelectSingleNode($".//*[contains(@class, '{fieldName}')]");
        var text = node?.InnerText.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : HttpUtility.HtmlDecode(text);
    }

    public void Dispose() => _http.Dispose();
}
