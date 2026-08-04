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
        // Kein globales Timeout — Downloads sind mehrere hundert MB (Solek: 1.28 GB).
        // Stattdessen setzen Fetch/Download eigene Cancel/Progress-Semantik.
        _http = new HttpClient();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"LSModManager/{version} (+https://github.com/Kroste/LS-ModManager)");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9,en;q=0.5");
        // GIANTS-CDN gibt Bilder und ZIPs nur mit Referer frei (sonst HTTP 403).
        _http.DefaultRequestHeaders.Referrer = new Uri(BaseUrl + "/");
    }

    /// <summary>
    /// Holt eine Seite des Katalogs. <paramref name="page"/> ist 1-basiert.
    /// Liefert bei Fehlern eine leere Liste — Fehler stehen im Log.
    /// </summary>
    public async Task<IReadOnlyList<ModHubEntry>> FetchCatalogPageAsync(
        int page = 1, string language = "de", CancellationToken ct = default,
        string? filter = null)
    {
        var url = BuildListUrl(page, language, filter);
        Log.Info("ModHub-Katalog laden: {url}", url);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var html = await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return ParseListPage(html);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn(ex, "ModHub-Fetch fehlgeschlagen: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
        catch (TaskCanceledException ex)
        {
            Log.Warn(ex, "ModHub-Fetch abgebrochen/Timeout: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
    }

    /// <summary>
    /// Holt die Rohtext-HTML einer Katalog-Seite — für Kategorien-Extraktion beim
    /// ersten Load. Statt einer zweiten Roundtrip wird beim RefreshCatalog eine
    /// Kopie der Bytes an <see cref="ParseCategories"/> weitergereicht.
    /// </summary>
    public async Task<string?> FetchCatalogPageHtmlAsync(int page, string language,
        CancellationToken ct = default, string? filter = null)
    {
        var url = BuildListUrl(page, language, filter);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            return await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-HTML-Fetch fehlgeschlagen: {url}", url);
            return null;
        }
    }

    /// <summary>
    /// Lädt die ZIP eines Mods direkt vom GIANTS-CDN und speichert sie in einer
    /// Temp-Datei. Ablauf: Detail-Seite holen → Download-URL mit passender mod_id
    /// per Regex extrahieren → ZIP streamen (Progress). ToS-konform, weil wir
    /// genau das tun, was ein Nutzerklick auch tut — nur ohne Browser.
    /// </summary>
    public async Task<ModDownloadResult> DownloadModAsync(
        int modId, string language, IProgress<ModDownloadProgress>? progress,
        CancellationToken ct = default, string? coverImageUrl = null)
    {
        var detailUrl = BuildDetailUrl(modId, language);
        Log.Info("Detail-Seite laden für Download: {url}", detailUrl);
        var html = await _http.GetStringAsync(detailUrl, ct).ConfigureAwait(false);

        var zipUrl = ExtractDownloadUrl(html, modId);
        if (zipUrl is null)
            throw new InvalidOperationException(
                $"Kein Download-Link für mod_id={modId} auf der Detail-Seite gefunden.");
        Log.Info("Download-URL für mod_id={id}: {url}", modId, zipUrl);

        var fileName = ExtractFileName(zipUrl, modId);
        // Landet im persistenten Downloads-Ordner, NICHT Temp. Der Nutzer soll
        // selbst entscheiden, wann/ob er installiert.
        var targetPath = Path.Combine(AppPaths.DownloadsDir, fileName);
        // Partial-Datei während Download, damit UI-Refresh keine halbe ZIP sieht.
        var partPath = targetPath + ".part";

        using var response = await _http.GetAsync(zipUrl,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        Log.Info("ZIP-Download beginnt: {name} ({size} Bytes) → {p}",
            fileName, total?.ToString() ?? "unbekannt", targetPath);

        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fileStream = File.Create(partPath))
        {
            var buffer = new byte[81_920];
            long done = 0;
            int read;
            var lastReport = DateTime.UtcNow;
            while ((read = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 200)
                {
                    progress?.Report(new ModDownloadProgress(done, total, fileName));
                    lastReport = now;
                }
            }
            progress?.Report(new ModDownloadProgress(done, total, fileName));
        }

        // Atomarer Umbenenn — jetzt sichtbar für ListDownloaded.
        if (File.Exists(targetPath)) File.Delete(targetPath);
        File.Move(partPath, targetPath);

        // Zusätzlich das Cover-Bild in den Preview-Cache legen. Viele Mods haben
        // NUR DDS-Icons in der ZIP (Avalonia rendert kein DDS), das ModHub-Cover
        // ist die einzige Chance auf ein Vorschaubild.
        if (string.IsNullOrWhiteSpace(coverImageUrl))
        {
            Log.Info("Kein Cover-URL für mod_id={id} übergeben — Preview wird leer bleiben.", modId);
        }
        else
        {
            try
            {
                var coverBytes = await _http.GetByteArrayAsync(coverImageUrl, ct).ConfigureAwait(false);
                // Extension aus Magic-Bytes ableiten — Skia auf Linux lehnt JPG
                // mit .png-Endung ab. Alte .png-Datei löschen wir vorher.
                var ext = AppPaths.GuessImageExtension(coverBytes);
                var oldPng = AppPaths.PreviewCacheBasePathFor(targetPath) + ".png";
                if (File.Exists(oldPng) && ext != ".png")
                    try { File.Delete(oldPng); } catch { /* best-effort */ }
                var previewCache = AppPaths.PreviewCacheBasePathFor(targetPath) + ext;
                await File.WriteAllBytesAsync(previewCache, coverBytes, ct).ConfigureAwait(false);
                Log.Info("Cover gecacht ({n} Bytes, {ext}): {p}", coverBytes.Length, ext, previewCache);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Cover-Download fehlgeschlagen: {url}", coverImageUrl);
            }
        }

        Log.Info("ZIP-Download fertig: {p}", targetPath);
        return new ModDownloadResult(targetPath, fileName);
    }

    /// <summary>
    /// Lädt die Detail-Seite und extrahiert Titel, Beschreibung, Screenshots und
    /// die vollständige Info-Tabelle (Kategorie/Autor/Version/Größe/Release).
    /// </summary>
    public async Task<ModHubDetail?> FetchModDetailAsync(
        int modId, string language, CancellationToken ct = default)
    {
        var url = BuildDetailUrl(modId, language);
        Log.Info("Detail laden: {url}", url);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var html = await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return ParseDetailPage(html, modId, url);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Detail-Fetch fehlgeschlagen: {url}", url);
            return null;
        }
    }

    /// <summary>Testbarer Detail-Parser (statisch, keine HTTP-Calls).</summary>
    public static ModHubDetail ParseDetailPage(string html, int modId, string detailUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var title = HtmlDecodeTrim(root.SelectSingleNode("//h2")?.InnerText) ?? $"Mod {modId}";

        // Info-Tabelle: <div class="table-row"><div>Label</div><div>Value</div></div>
        var infoRows = root.SelectNodes("//div[contains(@class,'table-game-info')]//div[contains(@class,'table-row')]");
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (infoRows is not null)
        {
            foreach (var row in infoRows)
            {
                var cells = row.SelectNodes(".//div[contains(@class,'table-cell')]");
                if (cells is null || cells.Count < 2) continue;
                var label = HtmlDecodeTrim(cells[0].InnerText)?.TrimEnd(':') ?? "";
                var value = HtmlDecodeTrim(cells[1].InnerText) ?? "";
                if (label.Length > 0) info[label] = value;
            }
        }

        var rating = HtmlDecodeTrim(root.SelectSingleNode("//div[contains(@class,'mod-item__rating-num')]")?.InnerText) ?? "";
        var description = ExtractDescription(root);
        var screenshots = ExtractScreenshots(root, modId);
        var downloadUrl = ExtractDownloadUrl(html, modId) ?? "";

        return new ModHubDetail(
            ModId: modId,
            Title: title,
            Author: PickInfo(info, "Autor", "Author"),
            Category: PickInfo(info, "Kategorie", "Category"),
            Version: PickInfo(info, "Version"),
            SizeText: PickInfo(info, "Grösse", "Größe", "Size"),
            ReleaseDate: PickInfo(info, "Veröffentlichung", "Release"),
            Platform: PickInfo(info, "Plattform", "Platform"),
            Filename: PickInfo(info, "Dateiname", "Filename"),
            RatingText: rating,
            DescriptionText: description,
            ScreenshotUrls: screenshots,
            DownloadUrl: downloadUrl,
            DetailUrl: detailUrl);
    }

    private static string PickInfo(Dictionary<string, string> info, params string[] keys)
    {
        foreach (var k in keys)
            if (info.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return "";
    }

    private static string ExtractDescription(HtmlNode root)
    {
        // Die Beschreibung liegt in einem Text-Container vor der Info-Tabelle.
        // GIANTS hat keine feste Klasse dafür — wir suchen das erste <p>/<div>
        // mit viel Text vor <div class="table-game-info">. Robust: alle p/div
        // durchgehen und den mit >100 Zeichen Fließtext nehmen.
        var candidates = root.SelectNodes(
            "//div[contains(@class,'mod-description') or contains(@class,'description')]");
        HtmlNode? best = null;
        if (candidates is not null)
            best = candidates.OrderByDescending(c => c.InnerText.Length).FirstOrDefault();

        if (best is null)
        {
            // Fallback: das textreichste div im "large-8"-Content-Bereich
            var contentCol = root.SelectSingleNode("//div[contains(@class,'large-8')]");
            best = contentCol;
        }
        if (best is null) return "";

        // <br> → \n, dann Tags entfernen, Whitespace normalisieren.
        var withBreaks = Regex.Replace(best.InnerHtml, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        withBreaks = Regex.Replace(withBreaks, @"</p>|</div>|</li>", "\n", RegexOptions.IgnoreCase);
        var stripped = Regex.Replace(withBreaks, @"<[^>]+>", "");
        var decoded = HttpUtility.HtmlDecode(stripped);
        // Mehrfach-Newlines auf max. 2 reduzieren
        decoded = Regex.Replace(decoded, @"\n[ \t]*\n[ \t]*(\n[ \t]*)+", "\n\n");
        return decoded.Trim();
    }

    private static IReadOnlyList<string> ExtractScreenshots(HtmlNode root, int modId)
    {
        var padded = modId.ToString("D8");
        var imgs = root.SelectNodes("//img[@src]");
        if (imgs is null) return Array.Empty<string>();
        var urls = new List<string>();
        foreach (var img in imgs)
        {
            var src = img.GetAttributeValue("src", "");
            if (!src.Contains("giants-software.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (!src.Contains("/modHub/storage/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!src.Contains(padded, StringComparison.Ordinal)) continue;
            if (src.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue; // Icon skippen
            if (!urls.Contains(src)) urls.Add(src);
        }
        return urls;
    }

    /// <summary>
    /// Sucht auf einer Detail-Seite den Download-Link, dessen CDN-Pfad die
    /// eigene mod_id enthält (die Detail-Seite listet oft auch „Ähnliche Mods"
    /// mit deren ZIP-URLs — die filtern wir raus).
    /// </summary>
    public static string? ExtractDownloadUrl(string detailHtml, int modId)
    {
        // ID ist im CDN-Pfad achtstellig zero-padded: /00352048/FS25_Solek.zip
        var padded = modId.ToString("D8");
        var pattern = @"href=""(https://cdn\d+\.giants-software\.com/modHub/storage/"
                    + padded + @"/[^""]+\.zip)""";
        var m = Regex.Match(detailHtml, pattern, RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Fallback: irgendeine ZIP, die die mod_id irgendwo im Pfad hat.
        m = Regex.Match(detailHtml,
            @"href=""(https://cdn\d+\.giants-software\.com/modHub/storage/\d+/[^""]+\.zip)""",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var candidate = m.Groups[1].Value;
        return candidate.Contains(padded, StringComparison.Ordinal) ? candidate : null;
    }

    private static string ExtractFileName(string url, int modId)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? $"FS25_mod_{modId}.zip" : name;
        }
        catch
        {
            return $"FS25_mod_{modId}.zip";
        }
    }

    /// <summary>
    /// Lädt das Cover-Bild einer bereits vorhandenen ZIP nach — nützlich fürs
    /// Backfill von Mods, die vor dem Cover-Feature installiert wurden, oder
    /// wenn der Download-Cover-Save aus irgendeinem Grund schiefging.
    /// </summary>
    public async Task<string?> EnsureCoverCachedAsync(string zipPath, string coverUrl,
        CancellationToken ct = default)
    {
        var existing = AppPaths.FindExistingPreview(zipPath);
        if (existing is not null) return existing;
        if (string.IsNullOrWhiteSpace(coverUrl)) return null;

        try
        {
            var bytes = await _http.GetByteArrayAsync(coverUrl, ct).ConfigureAwait(false);
            var ext = AppPaths.GuessImageExtension(bytes);
            if (ext == ".bin")
            {
                Log.Warn("Cover-URL lieferte kein Bild: {url}", coverUrl);
                return null;
            }
            var target = AppPaths.PreviewCacheBasePathFor(zipPath) + ext;
            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
            Log.Info("Cover-Backfill: {n} Bytes → {p}", bytes.Length, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Cover-Backfill fehlgeschlagen: {url}", coverUrl);
            return null;
        }
    }

    /// <summary>URL, die der Nutzer im Browser öffnen soll, um herunterzuladen.</summary>
    public string BuildDetailUrl(int modId, string language = "de") =>
        $"{BaseUrl}/mod.php?lang={language}&country=de&mod_id={modId}&title={GameTitleSlug}";

    internal static string BuildListUrl(int page, string language, string? filter = null)
    {
        // GIANTS ist 0-basiert: page=0 → Seite 1, page=1 → Seite 2, …
        // Unser page-Parameter ist 1-basiert (UI-freundlich), also mappen wir hier.
        var pageSuffix = page > 1 ? $"&page={page - 1}" : "";
        var filterSuffix = string.IsNullOrWhiteSpace(filter) ? "" : $"&filter={filter}";
        return $"{BaseUrl}{ListPath}?lang={language}&country=de&title={GameTitleSlug}{filterSuffix}{pageSuffix}";
    }

    /// <summary>
    /// Extrahiert alle GIANTS-Kategorien aus dem Katalog-HTML — Menüpunkte im
    /// KATEGORIE-Dropdown mit URL-Muster <c>filter=xxx</c> und lesbarem Label.
    /// </summary>
    public static IReadOnlyList<ModHubCategory> ParseCategories(string catalogHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(catalogHtml);
        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, 'filter=')]");
        if (anchors is null) return Array.Empty<ModHubCategory>();

        var result = new List<ModHubCategory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            var m = Regex.Match(href, @"filter=([a-zA-Z0-9_]+)");
            if (!m.Success) continue;
            var key = m.Groups[1].Value;
            if (!seen.Add(key)) continue;
            var label = HttpUtility.HtmlDecode(a.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(label)) continue;
            result.Add(new ModHubCategory(key, label));
        }
        return result;
    }

    /// <summary>
    /// Öffentlich testbar (parser-only, keine Netzwerk-Calls). Sucht alle
    /// Mod-Karten im GIANTS-Layout (<c>div.machines--mods</c>) und extrahiert
    /// Titel (h3), Autor (Von-Span), Preview-URL und die Rubrik (h4 im dlc__title,
    /// z.B. „EMPFOHLENER MOD" / „BELIEBTESTER MOD" / „NEU IM MODHUB").
    /// </summary>
    public static IReadOnlyList<ModHubEntry> ParseListPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var entries = new List<ModHubEntry>();
        var seen = new HashSet<int>();

        // GIANTS hat ZWEI Card-Layouts nebeneinander:
        //  - div.machines--mods = die 2 „hervorgehobenen" (EMPFOHLENER/BELIEBTESTER), h3-Titel
        //  - div.mod-item      = die eigentliche Katalog-Liste, h4-Titel, evtl. Label NEW!/UPDATE!
        var cards = doc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ',normalize-space(@class),' '),' machines--mods ') " +
            "or contains(concat(' ',normalize-space(@class),' '),' mod-item ')]")
                    ?? new HtmlNodeCollection(doc.DocumentNode);

        foreach (var card in cards)
        {
            var (modId, detailUrl) = FindModIdAnchor(card);
            if (modId is null) continue;
            if (!seen.Add(modId.Value)) continue;

            // Titel: h3 (machines--mods) oder h4 (mod-item)
            var title = HtmlDecodeTrim(card.SelectSingleNode(".//h3")?.InnerText)
                     ?? HtmlDecodeTrim(card.SelectSingleNode(".//div[contains(@class,'mod-item__content')]//h4")?.InnerText);
            var author = ExtractAuthor(card);
            // Rubrik: dlc__title h4 (Empfehlungen) oder mod-label (Katalog: NEW!/UPDATE!)
            var category = HtmlDecodeTrim(card.SelectSingleNode(".//div[contains(@class,'dlc__title')]//h4")?.InnerText)
                        ?? HtmlDecodeTrim(card.SelectSingleNode(".//div[contains(@class,'mod-label')]")?.InnerText);
            var previewUrl = ExtractPreview(card);

            entries.Add(new ModHubEntry(
                Title: title ?? $"Mod {modId}",
                Author: author,
                Category: category ?? "",
                PreviewUrl: previewUrl,
                DetailUrl: detailUrl,
                Version: null,
                SizeText: null));
        }

        // Fallback: Wenn die neue Card-Struktur nicht greift (Site-Redesign),
        // gehen wir zurück aufs alte anker-basierte Verfahren, damit wenigstens
        // die Detail-URLs erhalten bleiben.
        if (entries.Count == 0)
            return ParseListPageLegacy(doc, html);

        Log.Info("Katalog geparst: {n} Einträge", entries.Count);
        return entries;
    }

    private static (int? modId, string detailUrl) FindModIdAnchor(HtmlNode card)
    {
        var anchors = card.SelectNodes(".//a[contains(@href,'mod.php') and contains(@href,'mod_id=')]");
        if (anchors is null) return (null, "");
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            var m = Regex.Match(href, @"mod_id=(\d+)");
            if (!m.Success) continue;
            var id = int.Parse(m.Groups[1].Value);
            var absolute = new Uri(new Uri(BaseUrl), HttpUtility.HtmlDecode(href)).ToString();
            return (id, absolute);
        }
        return (null, "");
    }

    private static string ExtractAuthor(HtmlNode card)
    {
        // Struktur ist <p>...<span>Von: RajotGPLAY</span></p> (in machines__overview
        // oder mod-item__content). Kein „starts-with"-Filter — wir nehmen einfach
        // den ersten Span innerhalb des Content-Blocks und trimmen das Präfix ab.
        var span = card.SelectSingleNode(".//div[contains(@class,'machines__overview')]//span")
                   ?? card.SelectSingleNode(".//div[contains(@class,'mod-item__content')]//span");
        var text = HtmlDecodeTrim(span?.InnerText);
        if (text is null) return "";
        // "Von: XXX" oder "By: XXX" → "XXX"
        var idx = text.IndexOf(':');
        return idx >= 0 && idx + 1 < text.Length
            ? text.Substring(idx + 1).Trim()
            : text;
    }

    private static string ExtractPreview(HtmlNode card)
    {
        var img = card.SelectSingleNode(".//div[contains(@class,'machines__img')]//img")
                  ?? card.SelectSingleNode(".//div[contains(@class,'mod-item__img')]//img")
                  ?? card.SelectSingleNode(".//img");
        if (img is null) return "";
        var src = img.GetAttributeValue("data-src", "");
        if (string.IsNullOrWhiteSpace(src))
            src = img.GetAttributeValue("src", "");
        if (string.IsNullOrWhiteSpace(src)) return "";
        return src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? src
            : new Uri(new Uri(BaseUrl), src).ToString();
    }

    private static string? HtmlDecodeTrim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return HttpUtility.HtmlDecode(s).Trim();
    }

    /// <summary>Fallback für zerbrochene Card-Struktur — extrahiert nur mod_ids und Links.</summary>
    private static IReadOnlyList<ModHubEntry> ParseListPageLegacy(HtmlDocument doc, string html)
    {
        var entries = new List<ModHubEntry>();
        var seen = new HashSet<int>();
        foreach (Match m in Regex.Matches(html, @"mod\.php\?mod_id=(\d+)"))
        {
            var id = int.Parse(m.Groups[1].Value);
            if (!seen.Add(id)) continue;
            entries.Add(new ModHubEntry(
                Title: $"Mod {id}", Author: "", Category: "",
                PreviewUrl: "",
                DetailUrl: $"{BaseUrl}/mod.php?mod_id={id}&title=fs2025",
                Version: null, SizeText: null));
        }
        Log.Warn("Katalog-Parser Fallback aktiv — GIANTS-Site-Struktur verändert? ({n} IDs)", entries.Count);
        return entries;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Ergebnis eines abgeschlossenen Downloads (Zielpfad im Downloads-Ordner).</summary>
public sealed record ModDownloadResult(string TargetZipPath, string FileName);

/// <summary>GIANTS-Kategorie: URL-Key (<c>filter=xxx</c>) + lokalisiertes Label.</summary>
public sealed record ModHubCategory(string Filter, string Label);

/// <summary>Vollständige Detail-Ansicht eines ModHub-Mods (aus mod.php gescrapt).</summary>
public sealed record ModHubDetail(
    int ModId,
    string Title,
    string Author,
    string Category,
    string Version,
    string SizeText,
    string ReleaseDate,
    string Platform,
    string Filename,
    string RatingText,
    string DescriptionText,
    IReadOnlyList<string> ScreenshotUrls,
    string DownloadUrl,
    string DetailUrl);

/// <summary>Progress-Report während des Downloads.</summary>
public sealed record ModDownloadProgress(long BytesDone, long? BytesTotal, string FileName)
{
    public double? Fraction => BytesTotal is > 0 ? (double)BytesDone / BytesTotal.Value : null;
    public string FormatShort() =>
        BytesTotal is > 0
            ? $"{BytesDone / (1024d * 1024d):F1} / {BytesTotal.Value / (1024d * 1024d):F1} MB"
            : $"{BytesDone / (1024d * 1024d):F1} MB";
}
