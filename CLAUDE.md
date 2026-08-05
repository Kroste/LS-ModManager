# LS-ModManager

## Grundlagen

- **Was:** Mod-Manager für Landwirtschaftssimulator 25 — Mods installieren,
  aktivieren/deaktivieren, deinstallieren, Mod-Katalog aus drei Quellen
  (GIANTS-ModHub, Modhoster, Hof Hirschfeld) browsen.
- **Stack:** C# / .NET 10 / Avalonia 12.1, CommunityToolkit.Mvvm,
  Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking),
  HtmlAgilityPack, Pfim + SkiaSharp (DDS-Dekodierung), Microsoft.Extensions.Http
  + ProtectedData (KI-Baukasten), xunit.v3 + FluentAssertions 7.x.
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management,
  `Directory.Build.props`, MinVer (Tags `v*`).
- **Konventionen:** Alle Fenster erben von `ChromeWindow`, Kroste-Card-Look,
  GlobalExceptionHandler, TrayController Pflicht, `TreatWarningsAsErrors`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.
- **Repo:** `https://github.com/Kroste/LS-ModManager`
- **Lokaler Pfad:** `/home/OsteL/Entwicklung/LS-ModManager`

## Aktueller Stand (v0.2.0)

- Grundgerüst nach Kroste-Standard aufgesetzt (Directory.Build.props, CPM, slnx,
  CI + Release-Workflows, dependabot, FUNDING, App-Icon via Pillow-Script,
  packaging/linux inkl. AppImage-Build, .vscode/tasks.json).
- App-Rahmen: DI-Container, GlobalExceptionHandler, NLog mit
  `MaskingLayoutRenderer`, ViewLocator, ChromeWindow-Basisklasse + TitleBar-
  Control mit Avalonia-12-`ElementRole`-Rollen, TrayController (Minimize→Hide,
  Close→Exit).
- Domain: `Mod` (installiert/katalog), `ModMetadata` aus modDesc.xml,
  `ModDescReader` extrahiert Titel (DE→EN Fallback), Version, Autor, Preview-PNG.
  Fallback-Reihenfolge fürs Preview: `iconFilename.png` → `icon.png` →
  `store_*.png` → beliebiges `*.png` → **DDS-Dekodierung** (`iconFilename.dds`
  oder beliebiges `*.dds`) via `DdsToPngConverter`. PNG wird immer bevorzugt
  weil Store-Bilder kuratiert sind, DDS ist typisch nur das kleine In-Game-Icon.
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
  - `DdsToPngConverter`: statischer Helper, dekodiert DDS-Bytes (BC1/BC3/
    uncompressed BGRA/RGB) via Pfim zu rohen Pixeln und encodet die via
    SkiaSharp als PNG. **Stride-Falle:** Pfim liefert `IImage.Stride`, das
    je nach Format vom naiven `Width*BPP` abweichen kann (Padding-Alignment)
    — Wert 1:1 an SkiaSharp weiterreichen sonst gibt es sheared Bilder.
    **BGRA-Reihenfolge:** DDS legt Pixel in BGRA ab (nicht RGBA), daher
    `SKColorType.Bgra8888`. **Rgb24** wird auf Bgra8888 expandiert (Alpha=255)
    weil SkiaSharp kein 24-bit-BGR hat. **GCHandle-Pin** auf die Pixel-Bytes
    bis Encode fertig ist — SKBitmap.InstallPixels kopiert nicht, der Pointer
    muss stabil bleiben. **SkiaSharp-Version**: 3.119.4 (matcht Avalonia.Skia
    12.1.0 transitiv — mit älterer Version bricht `dotnet restore` mit
    NU1605-Downgrade-Fehler).
  - `AppSettingsService`: JSON unter `%APPDATA%` / `$XDG_CONFIG_HOME`, atomar
    (tmp+move). Defekte Datei wird als `settings.json.broken` gesichert, App
    startet mit Defaults weiter (Kroste-Persistenz-Regel).
  - `ModHubService`: HTTPS auf `farming-simulator.com/mods.php`, HTML-Parser mit
    HtmlAgilityPack (defensiv). **Drei** Card-Layouts: `.machines--mods`
    (Empfehlungen, `<h3>`), `.mod-item` (Katalog-Liste, `<h4>`) und
    `.dlc-featured--mods` (Featured-Slot pro Katalog-Seite, `<h3>` +
    Cover-URL im `style="background-image"`, Autor als „Von: …"-`<span>`).
    Der Featured-Container wird zusätzlich zu den regulären Cards geparst
    (`ParseFeaturedCard`) und die Einträge bekommen `IsFeatured=true`.
    Rotiert pro Katalog-Seite und über die Zeit, deshalb wird der Status
    bei jedem Full-Load-Refresh komplett neu vergeben (durch das
    `_allCatalog.Clear()` vor dem Refresh).
    **In-App-Download** direkt vom GIANTS-CDN via `DownloadModAsync`: Detail-URL
    holen → `ExtractDownloadUrl` sucht die ZIP mit passender mod_id (8-stellig
    zero-padded im CDN-Pfad) → GET mit Progress in Temp-Datei → an
    `ModInstallService.Install`. Kein Login/Session/Klick nötig. **Pflicht:
    `Referer`-Header setzen** — GIANTS-CDN blockt sonst mit HTTP 403 (auch für
    Preview-Bilder!). Der `UrlImageBehavior` und der `_http` im Service haben
    beide `Referer: https://www.farming-simulator.com/` als Default.
  - `HofHirschfeldCatalogService`: dritte Katalog-Quelle
    (`hof-hirschfeld.de`, Community-„Hirschfeld-Version"-Umbauten für LS25).
    Kein zentraler Endpoint — wir parsen die Startseite auf Kategorie-Slugs
    und iterieren pro Kategorie über die Paginierung (12 Mods pro Seite,
    typisch 1-2 Seiten pro Kategorie). HTML-Parsing mit HtmlAgilityPack
    (Karten sind `a.mod-card__media` mit `<img>` darin). Kein In-App-
    Download — die Site versteckt Downloads hinter einem Werbung-Consent-
    Overlay; wir öffnen die Detail-Seite im Browser. Author fix
    „Hof Hirschfeld", `Source = HofHirschfeldSource`.
  - `AppPaths.HasCatalogCoverCache` + `.catalog`-Sidecar-Marker:
    unterscheidet Katalog-Cover (JPG immer, PNG mit Marker) von ZIP-icon.png-
    Platzhaltern. Der Backfill triggert auf `!HasCatalogCoverCache`, nicht auf
    „kein Preview" — so bekommen auch Downloads mit ZIP-internem Icon das
    bessere CDN-Cover; und Endlos-Retrigger bei PNG-Coverdatei (Hof-Hirschfeld)
    wird verhindert.
  - `ModhosterCatalogService`: zweite Katalog-Quelle über die offene JSON-API
    `modhoster.de/mods.json?game_id=1` (game_id=1 ist bei modhoster LS25).
    24 Mods pro Seite, sequenzielles Paging mit 300 ms Delay. Kein In-App-
    Download (Modhoster braucht Login-Session, robots.txt sperrt `/external/`
    und `/redirect/`) — `ModHubEntry.CanInAppDownload=false`, UI zeigt statt
    „📥 Herunterladen" nur „🌐 Öffnen" und einen Source-Badge „Modhoster".
    Beide Quellen laufen im MainVM parallel als Background-Full-Load und
    mischen sich in `_allCatalog` (Dedup per DetailUrl); der persistente
    JSON-Cache enthält beide gemeinsam.
  - `UpdateService`: GitHub-Releases-API-Check + **echtes Self-Update**.
    Plattform-Detection (Win-ZIP, Linux-AppImage, Linux-tar.gz), Asset-Wahl
    aus dem Release-JSON, Download mit Progress in
    `%LOCALAPPDATA%\LSModManager\update\` bzw. `$XDG_STATE_HOME/LSModManager/update/`
    (NIE `AppContext.BaseDirectory` — AppImage-Squashfs ist read-only, siehe
    references/autoupdate.md Falle 2). Installer-Skripte werden on-the-fly
    geschrieben: `install.bat` mit `Wait-Process` + `xcopy` (Windows),
    `install-appimage.sh` mit `kill -0` + `cp -f` + `setsid` (AppImage,
    Inode-stabiler In-Place-Replace), `install-tarball.sh` mit `tar -xzf` +
    `chmod +x`. Nach Skript-Start `Environment.Exit(0)` + Kill-Fallback
    nach 1,5 s. Proxy-aware via `WebRequest.DefaultWebProxy`
    (Arbeitslaptop-Sophos).
- MainWindow: Header + **Erst-Start-Warnbanner** (sichtbar wenn
  `IsModPathMissing`, weist auf Einstellungen hin) + Toolbar-Sektionen
  (Spiel / Installation / System) + TabControl mit drei Tabs (Installiert /
  Mod-Katalog / Downloads) + Statusbar. Toolbar hat „🚜 LS25 starten" (Steam-URI) und „🔄 Updates prüfen".
  Card-basierter Look, kein Fluent-Grau. **Statusbar-Progress**: zwei
  ProgressBars an derselben Grid-Zelle, `IsVisible` toggled zwischen
  Indeterminate-Animation (Busy ohne Zahlwert) und Determinate mit echtem
  0..1-Wert. Reporter setzen `ProgressValue` und räumen im finally auf null
  zurück — sonst hängt der Balken auf 100 %. Statusbar rechts:
  „N installiert · X,X GB aktiv" — `TotalActiveSizeText` als VM-Computed,
  wird bei `RefreshInstalledAsync` nachnotified.
- **`ConfirmDialog`** (`Views/ConfirmDialog.axaml.cs`): wiederverwendbarer
  Ja/Nein-Dialog auf ChromeWindow-Basis. Statisches `ShowAsync(owner,
  title, message) → Task<bool>` — kein DI nötig. Aktuell genutzt beim
  Bulk-Uninstall (Toolbar-Button + Del-Shortcut).
- Installed-Tab: Live-Suche + Sortier-Dropdown (Name/Größe/Datum/Status) +
  Toggle „⬆ Nur mit Update" oben. `RebuildInstalledView` ist eine
  Filter+Sort-Pipeline über LINQ (Search-Text → Update-Filter → Sort).
  Sortier-Optionen sind `InstalledSortOption`-Records mit
  `LocalizedString`-Wrapper — ComboBox-Text schaltet bei Sprachwechsel live
  um ohne dass die Options-Liste neu gebaut werden muss.
  Multi-Selection per Ctrl-/Shift-Klick → Bulk-Toolbar mit
  „⏻ Alle aktivieren / deaktivieren / 🗑 alle deinstallieren"; Bulk-Uninstall
  fragt vorher über `ConfirmDialog` nach (Fehlklick-Schutz).
  Card-Buttons „⏻ (De-)Aktivieren" und „🗑 Deinstallieren" pro Mod, plus
  „⬇ Update installieren" (nur sichtbar bei HasUpdate, Katalog-Match und
  Version-Diff). Update-Ablauf: Download neu → alte deinstallieren → neue
  installieren → Enabled-State übernehmen. Doppelklick auf Card öffnet
  Detail-Fenster wenn ein Katalog-Match existiert (`TryShowInstalledDetails`),
  sonst still. Rechtsklick-`ContextMenu` pro Card: Details / Ordner im
  Dateimanager öffnen / Filename in Zwischenablage kopieren / Deinstallieren.
  Keyboard-Shortcuts im MainWindow-Code-Behind (nicht `Window.KeyBindings`,
  weil Focus/Dialog View-Concern): **F5** = Refresh, **Ctrl+F** = Fokus
  Suchfeld + SelectAll, **Del** = Bulk-Uninstall mit Dialog (nur wenn Focus
  in der ListBox — sonst wäre das im Suchfeld ein „Zeichen löschen"-
  Fehltritt).
- **Drag-and-Drop**: Beliebig viele .zip-Dateien auf's Fenster droppen →
  `InstallZipsAsync` installiert sie sequentiell, überspringt Non-ZIPs und
  ungültige Mod-Archive (Log-Warnung, User sieht Count in Statusbar).
  Avalonia-12-`DataTransfer`-API (`e.DataTransfer.Contains(DataFormat.File)`,
  `TryGetFiles()`).
- Katalog-Tab: Live-Suche (Titel/Autor/Kategorie) + Sortier-Dropdown
  (Standard/Name/Autor/Kategorie), Auto-Full-Load im Hintergrund (alle Seiten
  sequenziell mit 300 ms Delay, GIANTS hat keinen search-Parameter, daher
  clientseitig sammeln). Persistenter JSON-Cache unter
  `AppPaths.CacheRoot/catalog-<lang>.json` — beim App-Start instant geladen,
  inkrementeller Save alle 10 Seiten + im finally-Block (überlebt Crash /
  Close). Card-Buttons „📥 Herunterladen" und „👁 Details" (in-app).
  **Sortierung greift NUR beim expliziten Sort-Wechsel oder Rebuild**, nicht
  im `AppendToCatalogView` des Background-Full-Loads — sonst würden Positionen
  bei jedem Seiten-Nachlader umherspringen. Der User kann nach Full-Load
  erneut sortieren.
  **„⭐ EMPFOHLEN"-Badge:** Der Featured-Mod-Slot der GIANTS-Katalog-Seiten
  wird jetzt mit-geparst (siehe `ModHubService.ParseFeaturedCard`) und die
  entsprechenden Einträge kriegen `IsFeatured=true`. Im Katalog werden sie
  in der Sortierung IMMER nach oben priorisiert (unabhängig vom gewählten
  Sort-Key, via `OrderByDescending(IsFeatured).ThenBy(...)`). Gold-Badge
  auf der Card. Dedup respektiert den Featured-Status: wenn ein Mod auf
  einer Seite als reguläre Card UND auf einer anderen Seite als Featured
  auftaucht, wird der bestehende Eintrag per `with { IsFeatured = true }`
  aufgewertet (`TryMergeCatalogEntry`).
  **„NEU"-Badge:** Sidecar-Datei `catalog-<lang>-seen.txt` speichert die
  DetailUrls vom vorherigen App-Start (Textformat, eine URL pro Zeile —
  kürzer und diff-freundlicher als JSON). Beim aktuellen Start wird der
  Katalog dagegen gediffed, neue Einträge bekommen `IsNew=true` und ein
  grünes NEU-Badge. `SaveSeenSnapshot(lang)` läuft nach initialem Cache-Load
  UND am Ende des Full-Loads (`LoadAllRemainingPagesAsync`.finally) — sonst
  würden nach einem Refresh die neu geladenen Einträge beim nächsten Start
  fälschlich noch mal als „neu" markiert. Erst-Start ohne Sidecar-Datei:
  `_previousSeenUrls` ist null → `IsEntryNew` liefert immer false, kein
  Badge-Flood.
- Downloads-Tab: Alle heruntergeladenen ZIPs aus dem persistenten
  `AppPaths.DownloadsDir` (LocalAppData/cache/downloads bzw. XDG_CACHE) +
  Sortier-Dropdown (Name / Größe / Datum, Default „Datum" absteigend —
  neueste zuerst). Pro Card „📥 Installieren" (kopiert in Mod-Ordner) und
  „🗑 Löschen".
- ModDetailWindow: parst Detail-HTML von `mod.php?mod_id=…` (Titel, Autor,
  Kategorie, Version, Größe, Release, Rating, Beschreibung, Screenshots) und
  rendert alles in-App. „📥 Herunterladen"-Button delegiert an MainVM.
  **KI-Features** (sichtbar nur wenn Provider != None): Button „🤖
  Zusammenfassen" schickt Titel + Beschreibung an die KI (Prompt in
  `AiPromptBuilder.SummarizeSystemPrompt`), Antwort landet in eigener
  Gold-Card unterhalb der Beschreibungs-Card. Button „🤖 Ähnliche Mods
  finden" filtert `_allCatalog` auf gleiche Kategorie (max 30, sonst zu
  teuer), schickt Titelliste an die KI, mappt die 5 Titel-Antworten zurück
  auf ModHubEntries → Mini-Cards mit 🌐-Browser-Klick. Bewusst kein neues
  Detail-Fenster (unnötige Kette). Ohne Katalog-Kandidaten:
  SimilarNoResults-Meldung.
- **KI-Baukasten** (`Services/Ai/`): Multi-Provider-Fundament nach
  Kroste-Skill-Standard (`kroste-avalonia/assets/Ai/`) mit einer wichtigen
  Abweichung — `IAiProvider.CompleteAsync(system, user) → string` statt der
  Skill-eigenen `TranslateBatchAsync` (App-spezifisch: hier reicht
  Text-in-Text-out).
  - Provider: `OllamaProvider` (`/api/chat` + `/api/tags` + `/api/pull`-
    Streaming), `AnthropicProvider` (`/v1/messages` mit `x-api-key` +
    `anthropic-version`), `OpenAiCompatibleProvider` (deckt OpenAI, Mistral,
    Groq, LM Studio, OpenRouter etc. ab), `GeminiProvider`
    (`generateContent?key=`).
  - `AiSettingsService`: **eigene** `ai-settings.json` neben `settings.json`
    (getrennt vom `AppSettingsService` — keine Migration, keine
    Interference mit dem `.broken`-Backup).
  - `SecretProtection`: DPAPI auf Windows, AES mit MachineName+UserName-
    Binding auf Linux/macOS. Salt/Key-Ableitung projekt-spezifisch
    (`lsmodmanager-secret-v1`) — verhindert dass andere Kroste-Apps die
    Keys entschlüsseln.
  - `AiProviderFactory`: nimmt `IHttpClientFactory` (Kanal `ai` für
    Completions, `ai-pull` für Downloads), baut den passenden Provider aus
    aktueller `AiSettings`. Ollama-Timeout 10 min, Cloud-Provider 2 min.
  - `OllamaPullViewModel` + `OllamaPullWindow`: Streaming-Fortschritt vom
    Ollama-Pull mit Cancel; auf Settings-Fenster als modaler Dialog.
  - `AiPromptBuilder` (LS-spezifisch, NICHT im Baukasten): die zwei
    System-Prompts + User-Prompt-Builder + `ParseSimilarModTitles`-Helper
    für die Rück-Mappung der KI-Antwort.
  - `AiSummaryCache` (LS-spezifisch): Textdatei pro `modId` unter
    `AppPaths.AiSummariesCacheDir`. Zusammenfassungen werden beim
    Detail-Öffnen aus dem Cache vorbelegt — spart Tokens und Wartezeit.
    Kein Provider/Modell-Suffix bewusst, weil der Inhalt bei Wechsel nicht
    dramatisch anders wäre.
  - `ModDetailViewModel` hat eine History-Stack für „← Zurück"-Navigation
    zwischen empfohlenen Mods. `ShowSimilarDetailsAsync` push't den
    aktuellen State und ruft `LoadModAsync(newId)` — kein neues Fenster,
    keine Modal-Kette. `GoBackAsync` pop't und lädt den vorherigen Mod.
    KI-Buttons zeigen live-Text-Wechsel während der Anfrage
    (`SummarizeButtonText`, `FindSimilarButtonText`) statt nur den
    StatusText unten in der Statusbar.
  - `ModHubItemViewModel.IsNew` ist `[ObservableProperty]` + `MarkAsSeen()`:
    Katalog-Aktions-Commands im MainVM (`ShowDetails`, `OpenInBrowser`,
    `DownloadAsync`) resetten den NEU-Badge sobald der User den Mod
    interagiert. Persistent dank `SaveSeenSnapshot` bei Full-Load-Ende.
- SettingsWindow: Mod-Pfad (Auto-Detect + manueller Override mit Folder-Picker)
  + Katalog-Sprache (ComboBox) + **App-Sprache** (ComboBox mit Länderflaggen,
  Live-Umschaltung) + **KI-Integration**-Card (Provider-ComboBox, dynamisch
  ein-/ausgeblendete Endpoint/Model/API-Key-Felder je nach Wahl, „Verbindung
  testen"-Button, bei Ollama zusätzlich Empfehlungs-Dropdown mit Direkt-
  Download).
- AboutWindow: Version, GitHub-Link, BMC-Link, **„📁 Log-Ordner"-Button**
  (öffnet `AppContext.BaseDirectory/logs` im System-Dateimanager, erspart
  bei Support-Anfragen die Sucherei), „Auf Updates prüfen"-Button.
  Bei verfügbarem Update + passendem Plattform-Asset erscheint zusätzlich
  „⬇ Update auf vX installieren" mit Progress-Bar; nach Klick lädt die App
  das Asset, startet den Installer und beendet sich selbst.
- **L10N-Framework (DE + EN)**: `LocalizationService` als Singleton mit
  `Strings.resx` (EN neutral) und `Strings.de.resx`, `LocalizedString`-
  Wrapper (statischer Cache — Avalonia hält `Binding.Source` nicht dauerhaft
  stark, sonst würde ein pro-Binding-Wrapper GC'd), `TrExtension` als XAML-
  Markup-Extension (`{loc:Tr Key}`), `L.T`/`L.F`-Helper für Code-Behind
  und ViewModels. Live-Sprachwechsel via `NotifyAllChanged` — feuert
  `PropertyChanged(nameof(Value))` auf jedem gecachten Wrapper (WPF-Style
  `Item[]` funktioniert in Avalonia 12 unzuverlässig, siehe
  `LocalizationService.Current`-Doku). Zähler-Konstrukte („X installiert",
  „⬆ Update: v8.2.0") sind als Prefix/Suffix-Split gebaut (zwei
  TextBlocks + Zahl-Binding), damit Live-Wechsel ohne VM-Notify-Handler
  funktioniert. Für computed VM-Properties (`ModPathStatusText`) abonniert
  das MainWindowViewModel `LocalizationService.PropertyChanged` und
  dispatcht ein `OnPropertyChanged` auf den UI-Thread. Kategorien-Sentinel
  („Alle Kategorien") ist eine Factory-Method (nicht mehr static), damit
  bei Sprachwechsel Position 0 der `Categories`-Collection frisch ersetzt
  werden kann. Transiente `StatusText`-Meldungen bleiben in der Sprache,
  in der sie gesetzt wurden — werden bei nächster User-Aktion überschrieben.
  Neue Sprachen: `Strings.<iso>.resx` daneben legen und in
  `LocalizationService.SupportedCultures` eintragen (mit Flaggen-Emoji).
- **Backup/Restore der Mod-Konfiguration**: `ModBackupService.CreateBackupAsync`
  erzeugt ein selbstenthaltenes ZIP mit `manifest.json` (Format-Version 1,
  Enabled-States + Metadata pro Mod) und `mods/`-Unterordner (alle ZIPs,
  auch deaktivierte). Atomic write via tmp+move. `RestoreBackupAsync` liest
  das Manifest (lehnt unbekannte Format-Versionen ab, statt still Datenverlust
  zu riskieren), extrahiert jeden Mod in Temp und ruft `ModInstallService.Install`
  + `SetEnabled` je nach Manifest-State. **Wichtig:** beim Extract wird ein
  `.zip.disabled`-Filename auf `.zip` normalisiert — `Install` nutzt Filename
  1:1 und `SetEnabled(false)` hängt danach `.disabled` an; sonst landet
  `X.zip.disabled` als `X.zip.disabled.disabled`. UI: Buttons in der System-
  Toolbar (`💾 Backup` mit Save-Dialog, `📂 Restore` mit Open-Dialog), Progress
  in Statusbar.
- Tests: xunit.v3 — `ModDescReaderTests` (ZIP-Parsing, Sprach-Fallback,
  Preview-Extraktion inkl. DDS-Fallback + PNG-Priorität), `ModHubServiceTests`
  (URL-Builder, HTML-Parser), `ModPathServiceTests` (Plattform-Kandidaten),
  `ModBackupServiceTests` (Round-Trip Backup→leerräumen→Restore, Manifest-
  Inhalt, Ablehnung unbekannter Format-Version, InvalidOp bei leerem Ordner),
  `DdsToPngConverterTests` (unkomprimiertes BGRA-DDS mit generiertem Fixture,
  ungültiger/zu kurzer Input). DDS-Header-Builder ist `internal static` in
  `DdsToPngConverterTests` und wird von `ModDescReaderTests` mitverwendet —
  Layout mit Byte-Offsets kommentiert. Tests die `XDG_CONFIG_HOME` manipulieren
  müssen (Prozess-globale Variable!) brauchen `[Collection("EnvironmentIsolation")]`
  für sequentielle Ausführung — sonst race mit `AppSettingsBrokenBackupTests`.

## Roadmap

- **Kurzfristig (Quick-Wins):**
  - _(alle Quick-Wins der letzten Runde erledigt — Update-Install, DnD,
    Installed-Suche, .broken-Backup, Bulk-Aktionen)_

- **Mittelfristig:**
  - _(Backup/Restore + L10N-Framework erledigt)_

- **Groß (mehrere Runden):**
  - _(KI-Features komplett — Multi-Provider, Beschreibungs-Zusammenfassung,
    „Ähnliche Mods" erledigt in v0.4.0)_

- **Bekannt-aber-vertagt:**
  - `searchMod`-Parameter der Website — funktioniert nachgewiesen, wird aktuell
    nicht genutzt weil unser voller Katalog-Cache (~11000 Mods) schon alles
    findet. Bei Bedarf als „Live-Suche"-Button einbaubar.
  - **Multi-Profile** (Karriere-abhängige Mod-Sets): gestrichen — LS25 hat
    kein natives Profil-Konzept, Mods sind global. Der eigentliche Use-Case
    („mehrere Mod-Sets je Karriere") ist bereits mit Backup/Restore abgedeckt
    (ein Backup-ZIP pro Karriere, Restore vor Spielstart). In-App-Umschaltung
    wäre nur Kosmetik über einen Dropdown statt File-Dialog — Aufwand vs.
    Nutzen passt nicht.

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
  - DDS-Icons werden jetzt via Pfim + SkiaSharp dekodiert (`DdsToPngConverter`).
    Vorher: „bewusst nicht dekodiert, ein eigener Decoder wäre unverhältnismäßig"
    — mit Pfim (pure C#, MIT, ~50 KB) und dem Avalonia-transitiven SkiaSharp ist
    das aber ein 100-Zeilen-Helper. Fallback-Reihenfolge im `ModDescReader`: erst
    PNG-Alternativen (`iconFilename.png`, `icon.png`, `store_*.png`), dann DDS.
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
- **Single-file-Publish-Falle (IL3000):** `Assembly.Location` liefert in single-
  file-Apps immer einen leeren String — der Compiler warnt mit IL3000, und
  `TreatWarningsAsErrors` macht daraus einen Build-Fehler. Aufgeflogen beim
  v0.2.0-Release (Build lokal grün, Release-Workflow rot). Statt `Assembly.
  Location` immer `Environment.ProcessPath` nutzen — funktioniert in beiden
  Modi. Lokal reproduzieren mit `dotnet publish -c Release -r linux-x64
  --self-contained true -p:PublishSingleFile=true`.
- **Bekannte Grenzen:** ModHub-Parser bricht potenziell bei GIANTS-Site-Redesign
  — CSS-Selektoren sind bewusst tolerant, aber nicht immun. Neuen Selektor bei
  Bedarf in `ModHubService.ParseListPage` nachziehen. HTML-Fixture-Test bleibt
  grün auch bei kaputter Live-Seite.
- **Icon-Rebuild:** `python3 scripts/build_icon.py` — regeneriert PNG + ICO aus
  dem Pillow-Skript. Traktor-Motiv in Farming-Grün.
