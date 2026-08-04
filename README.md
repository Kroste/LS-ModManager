# LS-ModManager

[![CI](https://github.com/Kroste/LS-ModManager/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/LS-ModManager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/LS-ModManager)](https://github.com/Kroste/LS-ModManager/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Mod-Manager für **Landwirtschaftssimulator 25 (LS25/FS25)** — Desktop-App für
Windows und Linux (C# / .NET 10 / Avalonia 12).

<!-- Screenshot: docs/screenshot.png einfügen, sobald die UI steht -->

## Features

- **📦 Mods installieren:** ZIP auswählen oder direkt aus dem ModHub-Katalog
  herunterladen — die App kopiert die Datei in den korrekten LS25-Mod-Ordner
  und liest Titel/Autor/Version/Beschreibung aus der `modDesc.xml`.
- **🌐 ModHub-Katalog in-App:** Der komplette offizielle Katalog von
  [farming-simulator.com/mods.php](https://www.farming-simulator.com/mods.php)
  (aktuell ~4800 Mods) wird beim ersten Start im Hintergrund geladen und
  persistent gecacht — Suche und Filtern sind danach instant.
- **🔍 Suche und Kategorien:** Live-Textfilter (Titel/Autor/Kategorie) plus
  Auswahl aus den 154 GIANTS-Kategorien (Karten, Traktoren, Anhänger, …).
- **📥 Direkt herunterladen:** „Herunterladen"-Button pro Katalog-Karte lädt
  die ZIP samt Vorschaubild direkt vom offiziellen GIANTS-CDN. Der Download
  landet in einem persistenten Ordner (`Downloads`-Tab), von dort per Klick
  installierbar. Kein Browser-Umweg nötig.
- **👁 Detailansicht in-App:** Doppelklick oder „Details"-Button öffnet ein
  Fenster mit voller Beschreibung, allen Screenshots, Kategorie, Autor,
  Version, Größe, Bewertung. „Im Browser"-Button für den Fall der Fälle.
- **🔄 Update-Prüfung für installierte Mods:** „Updates prüfen" vergleicht die
  installierten Versionen mit dem Katalog. Bei neuerer Version erscheint
  Badge „⬆ Update: vX.Y" auf der Karte plus „⬇ Update installieren"-Button,
  der die neue Version lädt, die alte deinstalliert und die neue
  installiert (Aktiv/Deaktiviert-Status bleibt erhalten).
- **🚜 Spiel-Start aus der App:** „LS25 starten" ruft `steam://run/2300320`
  auf — Steam startet das Spiel, Windows und Linux (Proton).
- **⏻ Aktivieren/Deaktivieren:** Umbenennen zu `.zip.disabled` (LS25 ignoriert
  die Datei) — Mod bleibt vorhanden, wird aber nicht mehr geladen.
- **🖼 Vorschaubilder:** Icons aus der Mod-ZIP werden extrahiert; wenn die ZIP
  nur DDS-Icons enthält (viele Mods), holt die App das offizielle Cover vom
  ModHub-CDN als Fallback.
- **🐧 Linux-Support:** Erkennt automatisch den LS25-Mod-Ordner im
  Steam-Proton-Präfix, egal auf welcher Platte Steam-Library liegt
  (`libraryfolders.vdf` wird geparst). Manueller Override in den Einstellungen.
- **🖥 System-Tray:** Fenster schließen beendet, minimieren legt es ins Tray.
- **⚙ Einstellungen:** Mod-Pfad, Katalog-Sprache (DE/EN/FR/ES/IT/PL),
  Katalog-Auto-Refresh-Intervall (1 h / 6 h / 12 h / 24 h / 7 Tage / nie).
- **🔄 App-Update-Check:** Prüft GitHub-Releases (proxy-fähig) und meldet
  neue Versionen.

## Installation

Fertige Pakete gibt es auf der
[Releases-Seite](https://github.com/Kroste/LS-ModManager/releases):

**Windows:** `LSModManager-X.Y.Z-win-x64.zip` herunterladen, entpacken,
`LSModManager.exe` starten. Keine Installation nötig (self-contained, .NET ist
enthalten).

**Linux (AppImage, empfohlen):** `LSModManager-X.Y.Z-x86_64.AppImage`
herunterladen, ausführbar machen und starten:

```bash
chmod +x LSModManager-*-x86_64.AppImage
./LSModManager-*-x86_64.AppImage
```

**Linux (tar.gz):** `LSModManager-X.Y.Z-linux-x64.tar.gz` entpacken und
`./LSModManager` starten.

## Bedienung

1. **Beim ersten Start** wird der Mod-Ordner automatisch erkannt (Windows:
   `Dokumente\My Games\FarmingSimulator2025\mods`; Linux: Steam-Proton-Präfix
   auf allen Steam-Libraries). Falls die Erkennung fehlschlägt, in ⚙
   *Einstellungen* einen manuellen Pfad setzen. Der Katalog (~4800 Mods) wird
   im Hintergrund geladen — Statusbar zeigt den Fortschritt.
2. **Installierte Mods verwalten (Tab „📦 Installiert"):** Live-Suche oben
   (Titel/Autor/Dateiname). Karten mit Icon, Titel, Autor, Version, Status.
   Pro Karte rechts: „⏻ (De-)Aktivieren", „🗑 Deinstallieren", und bei
   verfügbarem Update zusätzlich „⬇ Update installieren".
3. **Neue Mods entdecken (Tab „🌐 ModHub-Katalog"):** Kategorie-Dropdown +
   Live-Suche links. Pro Karte „📥 Herunterladen" (lädt direkt in den
   Downloads-Ordner) und „👁 Details" (Detailfenster mit Screenshots und
   Beschreibung). Doppelklick auf eine Karte öffnet ebenfalls die Details.
4. **Heruntergeladen (Tab „⬇ Downloads"):** Liste aller heruntergeladenen
   ZIPs. Pro Karte „📥 Installieren" (kopiert in Mod-Ordner) und „🗑 Löschen".
5. **Manuelle ZIP-Installation:** Toolbar → „📦 ZIP installieren…" → Dateiwahl.
6. **Updates prüfen:** Toolbar → „🔄 Updates prüfen" — die App vergleicht alle
   installierten Versionen mit dem Katalog, markiert veraltete Mods mit einem
   Update-Badge und Update-Button.
7. **Spiel starten:** Toolbar → „🚜 LS25 starten" — öffnet LS25 über Steam.
8. **Ordner öffnen:** Toolbar → „📁 Mod-Ordner" bzw. „⬇ Downloads-Ordner".

## Einstellungen

- **LS25-Mod-Ordner:** Auto-Erkennung + manueller Override. Der erkannte Pfad
  wird angezeigt und kann per „Neu erkennen" aufgefrischt werden.
- **Katalog-Sprache:** DE/EN/FR/ES/IT/PL — bestimmt, in welcher Sprache Titel
  und Kategorien vom ModHub geladen werden.
- **Katalog-Cache-Refresh:** Wählbar zwischen „bei jedem Start neu",
  1 h / 6 h / 12 h / 24 h (Default) / 7 Tage / nie. Der ↺-Button im
  Katalog-Tab erzwingt immer einen Refresh.

Konfiguration liegt unter `%APPDATA%\LSModManager\settings.json` (Windows) bzw.
`~/.config/LSModManager/settings.json` (Linux). Katalog- und Preview-Cache
unter `%LOCALAPPDATA%\LSModManager\cache\` (Windows) bzw.
`~/.cache/LSModManager/` (Linux).

## Logs & Fehlersuche

Logdateien liegen im Unterordner `logs/` neben der Anwendung (Tagesarchiv,
14 Tage). Bei einem Problem bitte ein Issue mit der aktuellen Logdatei eröffnen —
Passwörter und Tokens werden automatisch maskiert.

## Entwicklung

```bash
dotnet build            # bauen
dotnet test             # Tests
dotnet run --project LSModManager
```

Release: VS-Code-Task „release (tag + push)" — prüft den Git-Zustand, setzt den
Tag und stößt die GitHub-Action an, die alle Pakete (win-x64 ZIP, linux-x64
tar.gz, AppImage) baut.

## Rechtliches

Diese App ist **kein offizielles Produkt** von GIANTS Software. Sie greift auf
den öffentlichen ModHub-Katalog nur mit gebremster Rate lesend zu, der eigentliche
Download läuft immer über den Browser des Nutzers (User-initiiert, ToS-konform).

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ Gefällt dir das Tool? [Buy me a coffee](https://buymeacoffee.com/kroste)
