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
    Linux scannt Steam-Proton-Präfixe in allen bekannten Library-Roots
    (`.steam`, `.local/share/Steam`, Flatpak).
  - `ModInstallService`: List/Install/Uninstall/Enable-Toggle (via
    `.zip.disabled`-Suffix). Cached Preview-PNGs unter LocalAppData/cache.
  - `AppSettingsService`: JSON unter `%APPDATA%` / `$XDG_CONFIG_HOME`, atomar.
  - `ModHubService`: HTTPS auf `farming-simulator.com/mods.php`, HTML-Parser mit
    HtmlAgilityPack (defensiv gegenüber Struktur-Änderungen). Download läuft
    **nie** hier — die UI öffnet die Detail-URL im Browser (ToS-konform).
  - `UpdateService`: GitHub-Releases-API-Check (proxy-aware), noch kein
    Self-Update (Phase 2 laut references/autoupdate.md).
- MainWindow: Header + Toolbar-Sektionen (Installation / ModHub / System) +
  TabControl mit „Installiert" und „ModHub-Katalog" + Statusbar. Card-basierter
  Look, kein Fluent-Grau, keine hardcoded Hex-Farben.
- SettingsWindow: Mod-Pfad (Auto-Detect + manueller Override mit Folder-Picker)
  + Katalog-Sprache (ComboBox).
- AboutWindow: Version, GitHub-Link, BMC-Link, „Auf Updates prüfen"-Button.
- Tests: xunit.v3 — `ModDescReaderTests` (ZIP-Parsing, Sprach-Fallback,
  Preview-Extraktion), `ModHubServiceTests` (URL-Builder, HTML-Parser),
  `ModPathServiceTests` (Plattform-Kandidaten).

## Roadmap

- **Kurzfristig:**
  - Drag-and-Drop von ZIPs auf das Fenster.
  - Filter/Suche in beiden Tabs (Text-Suche + Kategorie-Filter).
  - Paginierung im Katalog (aktuell nur Seite 1).
  - Bulk-Aktionen (mehrere Mods gleichzeitig aktivieren/deaktivieren).
- **Mittelfristig:**
  - Vollständiges Self-Update nach `references/autoupdate.md`
    (Windows-ZIP-Install-Skript, AppImage-Ersetzung, tar.gz-Rebuild).
  - Backup/Restore der Mod-Konfiguration.
  - Multi-Profile (Karriere-abhängige Mod-Sets).
  - Optional: DDS-Decoder für Icons, damit auch Mods ohne PNG-Icon ein
    Vorschaubild bekommen (BC1/BC3-Decode nötig).
- **Langfristig / KI-Idee:** Optional (Ollama-Default) automatische
  Zusammenfassung von Mod-Beschreibungen und Empfehlungssystem („ähnliche Mods
  wie X"). Kroste-KI-Standard (Multi-Provider, Settings-UI).

## Referenz

- **Architektur-Entscheidungen:**
  - Kein WebView / kein Auto-Download: Wir scrapen die ModHub-Seite nur lesend
    (Katalog anzeigen), Downloads gehen immer über den Browser des Nutzers. Das
    ist die einzige ToS-konforme Variante ohne offizielle Modhub-API.
  - DDS-Icons werden bewusst nicht dekodiert — Avalonia kann DDS nicht nativ
    rendern und ein eigener Decoder wäre unverhältnismäßig. Wir suchen nach
    PNG-Alternativen in der ZIP (`icon.png`, `store_*.png`).
  - `.zip.disabled` als Deaktivierungs-Konvention: LS25 lädt ausschließlich
    Dateien mit exakter `.zip`-Endung. Die Datei bleibt so im Ordner, wird aber
    ignoriert — kein Löschen nötig.
  - Steam-Proton-Auto-Detect scannt alle `compatdata/*/pfx/…`-Präfixe. Wir
    prüfen nicht auf eine bestimmte App-ID, weil GIANTS die Steam-ID
    unangekündigt ändern kann.
- **Wichtige Klassen:** `ModDescReader` (ZIP → Metadata),
  `ModHubService.ParseListPage` (statisch, testbar),
  `MainWindowViewModel` (dünn, delegiert alles),
  `UrlImageBehavior` (async URL→Image für Katalog-Cards).
- **Bekannte Grenzen:** ModHub-Parser bricht potenziell bei GIANTS-Site-Redesign
  — CSS-Selektoren sind bewusst tolerant, aber nicht immun. Neuen Selektor bei
  Bedarf in `ModHubService.ParseListPage` nachziehen. HTML-Fixture-Test bleibt
  grün auch bei kaputter Live-Seite.
- **Icon-Rebuild:** `python3 scripts/build_icon.py` — regeneriert PNG + ICO aus
  dem Pillow-Skript. Traktor-Motiv in Farming-Grün.
