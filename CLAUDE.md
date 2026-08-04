# LS-ModManager

## Grundlagen

- **Was:** Mod-Manager für Landwirtschaftssimulator 25 — Mods installieren,
  aktivieren/deaktivieren, deinstallieren, ModHub-Katalog browsen.
- **Stack:** C# / .NET 10 / Avalonia 12.1, CommunityToolkit.Mvvm,
  Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking),
  HtmlAgilityPack, xunit.v3 + FluentAssertions 7.x.
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management,
  `Directory.Build.props`, MinVer (Tags `v*`).
- **Konventionen:** Alle Fenster erben von `ChromeWindow`, Kroste-Card-Look,
  GlobalExceptionHandler, TrayController Pflicht, `TreatWarningsAsErrors`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.
- **Repo:** `https://github.com/Kroste/LS-ModManager`
- **Lokaler Pfad:** `/home/OsteL/Entwicklung/LS-ModManager`

## Aktueller Stand (v0.1.0 initial)

- Grundgerüst nach Kroste-Standard aufgesetzt (Directory.Build.props, CPM, slnx,
  CI + Release-Workflows, dependabot, FUNDING, App-Icon via Pillow-Script,
  packaging/linux inkl. AppImage-Build, .vscode/tasks.json).
- App-Rahmen: DI-Container, GlobalExceptionHandler, NLog mit
  `MaskingLayoutRenderer`, ViewLocator, ChromeWindow-Basisklasse + TitleBar-
  Control mit Avalonia-12-`ElementRole`-Rollen, TrayController (Minimize→Hide,
  Close→Exit).
- Domain: `Mod` (installiert/katalog), `ModMetadata` aus modDesc.xml,
  `ModDescReader` extrahiert Titel (DE→EN Fallback), Version, Autor, Preview-PNG
  (icon.png / store_*.png — DDS wird bewusst nicht gelesen).
- Services:
  - `ModPathService`: Windows `Documents/My Games/FarmingSimulator2025/mods`,
    Linux scannt Steam-Proton-Präfixe in **allen** Library-Roots. Roots kommen
    aus `libraryfolders.vdf` (authoritativ, deckt Zusatzplatten wie
    `/run/media/system/Games/SteamLibrary/` ab) plus typische Home-Locations
    (`.steam`, `.local/share/Steam`, Flatpak) plus Mount-Point-Scan
    (`/run/media/*/*`, `/mnt/*`) als Fallback. Bazzite-Falle: VDF listet
    `/var/home/…`, wir mappen zusätzlich auf `/home/…`. Proton nutzt den
    XP-Ordnernamen `My Documents` — beide (`Documents` und `My Documents`)
    werden probiert. Detection prüft den FS25-**Spielordner**, nicht den
    `mods`-Unterordner (den legt der erste Install an).
  - `ModInstallService`: List/Install/Uninstall/Enable-Toggle (via
    `.zip.disabled`-Suffix). Cached Preview-PNGs unter LocalAppData/cache.
  - `AppSettingsService`: JSON unter `%APPDATA%` / `$XDG_CONFIG_HOME`, atomar.
  - `ModHubService`: HTTPS auf `farming-simulator.com/mods.php`, HTML-Parser mit
    HtmlAgilityPack (defensiv). Zwei Card-Layouts: `.machines--mods`
    (Empfehlungen, `<h3>`) und `.mod-item` (Katalog-Liste, `<h4>`).
    **In-App-Download** direkt vom GIANTS-CDN via `DownloadModAsync`: Detail-URL
    holen → `ExtractDownloadUrl` sucht die ZIP mit passender mod_id (8-stellig
    zero-padded im CDN-Pfad) → GET mit Progress in Temp-Datei → an
    `ModInstallService.Install`. Kein Login/Session/Klick nötig. **Pflicht:
    `Referer`-Header setzen** — GIANTS-CDN blockt sonst mit HTTP 403 (auch für
    Preview-Bilder!). Der `UrlImageBehavior` und der `_http` im Service haben
    beide `Referer: https://www.farming-simulator.com/` als Default.
  - `UpdateService`: GitHub-Releases-API-Check (proxy-aware), noch kein
    Self-Update (Phase 2 laut references/autoupdate.md).
- MainWindow: Header + Toolbar-Sektionen (Spiel / Installation / System) +
  TabControl mit drei Tabs (Installiert / ModHub-Katalog / Downloads) +
  Statusbar mit Live-Progress. Toolbar hat „🚜 LS25 starten" (Steam-URI) und
  „🔄 Updates prüfen". Card-basierter Look, kein Fluent-Grau.
- Installed-Tab: Live-Suche oben (Titel/Autor/Filename, filtert `_allInstalled`
  in `InstalledMods`). Card-Buttons „⏻ (De-)Aktivieren" und „🗑 Deinstallieren"
  pro Mod, plus „⬇ Update installieren" (nur sichtbar bei HasUpdate, Katalog-
  Match und Version-Diff). Update-Ablauf: Download neu → alte deinstallieren
  → neue installieren → Enabled-State übernehmen. Doppelklick auf Katalog-
  Card öffnet Detail-Fenster.
- **Drag-and-Drop**: Beliebig viele .zip-Dateien auf's Fenster droppen →
  `InstallZipsAsync` installiert sie sequentiell, überspringt Non-ZIPs und
  ungültige Mod-Archive (Log-Warnung, User sieht Count in Statusbar).
  Avalonia-12-`DataTransfer`-API (`e.DataTransfer.Contains(DataFormat.File)`,
  `TryGetFiles()`).
- Katalog-Tab: Live-Suche (Titel/Autor/Kategorie), Auto-Full-Load im Hintergrund
  (alle Seiten sequenziell mit 300 ms Delay, GIANTS hat keinen search-Parameter,
  daher clientseitig sammeln). Persistenter JSON-Cache unter
  `AppPaths.CacheRoot/catalog-<lang>.json` — beim App-Start instant geladen,
  inkrementeller Save alle 10 Seiten + im finally-Block (überlebt Crash /
  Close). Card-Buttons „📥 Herunterladen" und „👁 Details" (in-app).
- Downloads-Tab: Alle heruntergeladenen ZIPs aus dem persistenten
  `AppPaths.DownloadsDir` (LocalAppData/cache/downloads bzw. XDG_CACHE). Pro
  Card „📥 Installieren" (kopiert in Mod-Ordner) und „🗑 Löschen".
- ModDetailWindow: parst Detail-HTML von `mod.php?mod_id=…` (Titel, Autor,
  Kategorie, Version, Größe, Release, Rating, Beschreibung, Screenshots) und
  rendert alles in-App. „📥 Herunterladen"-Button delegiert an MainVM.
- SettingsWindow: Mod-Pfad (Auto-Detect + manueller Override mit Folder-Picker)
  + Katalog-Sprache (ComboBox).
- AboutWindow: Version, GitHub-Link, BMC-Link, „Auf Updates prüfen"-Button.
- Tests: xunit.v3 — `ModDescReaderTests` (ZIP-Parsing, Sprach-Fallback,
  Preview-Extraktion), `ModHubServiceTests` (URL-Builder, HTML-Parser),
  `ModPathServiceTests` (Plattform-Kandidaten).

## Roadmap

- **Kurzfristig (Quick-Wins):**
  - **Bulk-Aktionen** — Multi-Selection in Installed-Liste + „Alle
    aktivieren/deaktivieren/deinstallieren".
- **Mittelfristig (Kroste-Standard-Pflichten + solide Ergänzungen):**
  - **Vollständiges Self-Update** nach `references/autoupdate.md`
    (Windows-ZIP-Install-Batch, AppImage-Ersetzung, tar.gz-Rebuild). Aktuell
    haben wir nur den Check — reine Notification ist gegen den Kroste-Standard.
  - **`.broken`-Backup im `AppSettingsService`** — defekte Settings-Datei als
    `.broken` sichern statt still mit Defaults zu überschreiben (Kroste-
    Persistenz-Regel).
  - **Backup/Restore der Mod-Konfiguration** (ZIP mit aktiven Mods + Meta).
- **Groß (mehrere Runden):**
  - **Multi-Profile** (Karriere-abhängige Mod-Sets).
  - **DDS-Decoder** für Icons (BC1/BC3, evtl. mit Pfim-NuGet), damit Mods ohne
    PNG-Icon trotzdem eine Preview haben (aktuell kompensiert der ModHub-
    Cover-Backfill das für alle im Katalog gelisteten Mods).
  - **KI-Features** nach Allpaca-Muster (Multi-Provider, Ollama-Default):
    Beschreibungs-Zusammenfassung, Empfehlungssystem („Ähnliche Mods wie X").
- **Bekannt-aber-vertagt:**
  - `searchMod`-Parameter der Website — funktioniert nachgewiesen, wird aktuell
    nicht genutzt weil unser voller Katalog-Cache (~4800 Mods) schon alles
    findet. Bei Bedarf als „Live-Suche"-Button einbaubar.

## Referenz

- **Architektur-Entscheidungen:**
  - **In-App-Download** (ohne WebView): Wir laden die Mod-ZIP direkt vom GIANTS-
    CDN, indem wir 1:1 nachbauen, was ein Nutzerklick auf „Download" auf der
    Detail-Seite tun würde — GET auf die Detail-URL, ZIP-URL parsen, GET auf die
    CDN-URL mit Referer. Keine Login-Session, kein Rate-Limit-Umgehen, User-Agent
    ist transparent (`LSModManager/x.y (+github…)`). Das ist rechtlich sauber:
    wir imitieren keinen Browser und keinen anderen Nutzer. Die alternative
    „Browser öffnen"-Route bleibt als Ghost-Button „🌐 Details" erhalten, damit
    der User bei komplexeren Fällen (Kommentare lesen, Screenshots) selbst auf
    die Detail-Seite gehen kann.
  - DDS-Icons werden bewusst nicht dekodiert — Avalonia kann DDS nicht nativ
    rendern und ein eigener Decoder wäre unverhältnismäßig. Wir suchen nach
    PNG-Alternativen in der ZIP (`icon.png`, `store_*.png`).
  - `.zip.disabled` als Deaktivierungs-Konvention: LS25 lädt ausschließlich
    Dateien mit exakter `.zip`-Endung. Die Datei bleibt so im Ordner, wird aber
    ignoriert — kein Löschen nötig.
  - Steam-Proton-Auto-Detect scannt alle `compatdata/*/pfx/…`-Präfixe **in
    allen Library-Roots**. Wir prüfen nicht auf eine bestimmte App-ID, weil
    GIANTS die Steam-ID unangekündigt ändern kann (FS25 ist derzeit AppID
    `2300320`, FS22 war `1248130`). Library-Roots kommen primär aus
    `libraryfolders.vdf` — externe Platten wie `/run/media/system/Games/
    SteamLibrary/` werden so ohne Konfiguration erkannt.
  - Proton legt Dateien im Präfix mit dem XP-Style-Ordnernamen `My Documents`
    an, nicht `Documents`. Der Path-Service probiert beide — wenn wir das mal
    weiter portieren, immer beide Namen berücksichtigen.
- **Wichtige Klassen:** `ModDescReader` (ZIP → Metadata),
  `ModHubService.ParseListPage` + `ParseDetailPage` + `ExtractDownloadUrl`
  (alle statisch, testbar), `AppPaths` (zentrale Cache/Downloads-Pfade),
  `MainWindowViewModel` (dünn, delegiert alles; `DetailRequested`-Event
  entkoppelt VM von View-Instanziierung), `ModDetailViewModel` (lädt
  Detail-Seite async), `UrlImageBehavior` (async URL→Image, mit Referer).
- **UI-Konventionen:**
  - VM darf keine Views instanziieren → `MainWindowViewModel.DetailRequested`
    ist ein `event Action<ModHubItemViewModel>`, `MainWindow.axaml.cs` lauscht
    über `DataContextChanged` und öffnet `ModDetailWindow` per `ShowDialog`.
  - Downloads landen NIE in `Path.GetTempPath()` — immer
    `AppPaths.DownloadsDir` (persistent, LocalAppData/cache). Sonst räumt der
    OS-Temp-Cleaner die halb-installierte ZIP weg.
  - Card-Buttons pro Katalog-Eintrag nutzen `$parent[Window].((vm:…)DataContext).XxxCommand`
    plus `CommandParameter="{Binding}"` — so kann jede Card ihren eigenen
    Handler auslösen ohne über `SelectedCatalog` gehen zu müssen.
  - Beim Background-Full-Load ist **inkrementelles `CatalogMods.Add(...)` Pflicht**
    (via `AppendToCatalogView`). `Clear() + Add()` bei jeder Seite (300 ms Takt)
    flimmert die ListBox sichtbar bei jeder Aktualisierung. Für Suchtext-Wechsel
    oder Refresh bleibt der Full-Rebuild.
- **Skia-Bug auf Linux (Bazzite bestätigt):** Bitmap lädt ein JPG **NICHT**,
  wenn die Datei `.png`-Endung hat — obwohl Skia intern Magic-Bytes prüft. Fehler:
  `Unable to load bitmap from provided data`. Gilt für Cover-Downloads vom
  GIANTS-CDN (immer JPG). Lösung: `AppPaths.GuessImageExtension(bytes)` gibt die
  echte Extension zurück (`.jpg` / `.png`), Cache-File wird mit dieser Endung
  gespeichert. `AppPaths.FindExistingPreview(zipPath)` probiert beim Lesen beide
  Endungen. Fällt bei `.png` mit tatsächlichem JPG-Inhalt auf `Auto-Delete` in
  `InstalledModItemViewModel.LoadPreview`.
- **Bekannte Grenzen:** ModHub-Parser bricht potenziell bei GIANTS-Site-Redesign
  — CSS-Selektoren sind bewusst tolerant, aber nicht immun. Neuen Selektor bei
  Bedarf in `ModHubService.ParseListPage` nachziehen. HTML-Fixture-Test bleibt
  grün auch bei kaputter Live-Seite.
- **Icon-Rebuild:** `python3 scripts/build_icon.py` — regeneriert PNG + ICO aus
  dem Pillow-Skript. Traktor-Motiv in Farming-Grün.
