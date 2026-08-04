# LS-ModManager

[![CI](https://github.com/Kroste/LS-ModManager/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/LS-ModManager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/LS-ModManager)](https://github.com/Kroste/LS-ModManager/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Mod-Manager für **Landwirtschaftssimulator 25 (LS25/FS25)** — Desktop-App für
Windows und Linux (C# / .NET 10 / Avalonia 12).

<!-- Screenshot: docs/screenshot.png einfügen, sobald die UI steht -->

## Features

- **📦 Mods installieren:** ZIP auswählen, die App kopiert sie in den korrekten
  LS25-Mod-Ordner und liest Titel/Autor/Version/Beschreibung aus der `modDesc.xml`.
- **🗑 Deinstallieren:** Ausgewählten Mod vom Datenträger entfernen.
- **⏻ Aktivieren/Deaktivieren:** Umbenennen zu `.zip.disabled` (LS25 ignoriert die
  Datei) — Mod bleibt vorhanden, wird aber nicht mehr geladen.
- **🖼 Vorschaubilder:** Icons aus der Mod-ZIP werden automatisch extrahiert.
- **🌐 ModHub-Katalog:** Browsen des offiziellen Katalogs von
  [farming-simulator.com/mods.php](https://www.farming-simulator.com/mods.php).
  Der eigentliche Download läuft aus rechtlichen Gründen (GIANTS-ToS) über den
  Browser — Klick auf „Im Browser öffnen", Nutzer lädt die ZIP, dann
  „ZIP installieren…" in der App.
- **🐧 Linux-Support:** Erkennt automatisch den LS25-Mod-Ordner im Steam-Proton-
  Präfix. Manueller Override in den Einstellungen möglich.
- **🖥 System-Tray:** Fenster schließen beendet, minimieren legt es ins Tray.
- **🔄 Update-Check:** Prüft GitHub-Releases (proxy-fähig) und meldet neue Versionen.

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
   `Dokumente\My Games\FarmingSimulator2025\mods`; Linux: Steam-Proton-Präfix).
   Falls die Erkennung fehlschlägt, in ⚙ *Einstellungen* einen manuellen Pfad
   setzen.
2. **Installierte Mods:** Tab „📦 Installiert" — Karten mit Icon, Titel, Autor,
   Version und Status. Rechte Toolbar-Buttons wirken auf die aktuelle Auswahl.
3. **Mod hinzufügen:** Toolbar → „📦 ZIP installieren…" → beliebige Mod-ZIP
   auswählen. Die App validiert, dass eine `modDesc.xml` enthalten ist.
4. **Neue Mods entdecken:** Tab „🌐 ModHub-Katalog" → „Katalog laden". Auf einen
   Mod klicken → „Im Browser öffnen" → Download starten. Anschließend in der App
   „ZIP installieren…" mit der heruntergeladenen Datei.
5. **Mod-Ordner öffnen:** Toolbar → „📁 Ordner öffnen" — Systemexplorer springt
   dorthin.

## Einstellungen

- **LS25-Mod-Ordner:** Auto-Erkennung + manueller Override. Der erkannte Pfad
  wird angezeigt und kann per „Neu erkennen" aufgefrischt werden.
- **Katalog-Sprache:** DE/EN/FR/ES/IT/PL — bestimmt, in welcher Sprache Titel und
  Kategorien vom ModHub geladen werden.

Konfiguration liegt unter `%APPDATA%\LSModManager\settings.json` (Windows) bzw.
`~/.config/LSModManager/settings.json` (Linux).

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
