# Bifrost Updater

Ein Auto-Updater für BepInEx-Mods, der ohne Einstellungen auskommt. Beim Start des Spiels findet er die installierten Mods selbst, fragt nach neueren Versionen und ersetzt die Dateien. Es gibt keine Liste, die du pflegen musst. Er läuft unter **Windows und Linux** (auch mit Proton und auf dedizierten Servern).

Der Code ist nicht auf Valheim festgelegt, getestet ist er aber nur mit Valheim.

## So funktioniert es

Der Updater ist ein **BepInEx-Patcher**. Patcher laufen, bevor BepInEx irgendein Plugin lädt. Eine aktualisierte Mod ist deshalb schon im selben Spielstart die neue Version, und es gibt keine Probleme mit gesperrten Dateien unter Windows. Ein Update beim Schließen des Spiels wäre dagegen unsicher: Unter Windows ist die DLL dann noch gesperrt, und ein Absturz würde das Update verlieren.

### Wie er Mods findet und die Quelle bestimmt

Er sucht in `BepInEx/plugins` und `BepInEx/patchers` und ordnet jeder Mod eine Quelle zu, in dieser Reihenfolge:

1. **Config:** Eine `Mod`-Zeile nennt die Quelle ausdrücklich (nur nötig für Sonderfälle).
2. **Eingebaute Quelle:** Die DLL nennt ihr Repository selbst (`AssemblyMetadata("UpdateRepo", "Owner/Repo")`, siehe [Eigene Mods anschließen](#eigene-mods-anschließen)).
3. **Thunderstore:** Mods aus r2modman oder Gale liegen in einem Ordner `Namespace-Name` mit `manifest.json`. Der Updater fragt Thunderstore nach der neuesten Version.
4. **GitHub-Konto der Vertrauten:** Ein BepInEx-Plugin ohne Angabe wird mit den öffentlichen Repositories der vertrauten GitHub-Nutzer verglichen. Der Name des Repositories muss dem DLL-Namen gleichen oder ihn enthalten (Groß-/Kleinschreibung und Trennzeichen egal) und der einzige Treffer sein. So wird `KongStrong.dll` dem Repo `valheim-kong-strong` zugeordnet. Eine falsche Zuordnung richtet nichts an: Das Release-Zip muss dieselbe DLL enthalten, und die Prüfsumme muss passen.

Standardmäßig vertraut er dem Besitzer, der in seiner eigenen DLL als Quelle steht (bei dieser Version Stanni97).

### Was pro Start passiert

1. Alle Mods werden erkannt und die Quellen zugeordnet. Neue Mods brauchen keine Eintragung.
2. Pro Mod wird die neueste Version abgefragt, höchstens alle 6 Stunden (`CheckIntervalHours`).
3. Ist sie neuer, hängt es vom Besitzer ab: Mods **vertrauter Besitzer** werden aktualisiert. Bei allen anderen schreibt der Updater nur eine Zeile ins Log (`ThirdParty = Notify`), außer du stellst `ThirdParty = Update` ein.
4. Bei GitHub muss die SHA-256-Prüfsumme des Downloads zu GitHubs Angabe passen. Aus dem Zip werden nur die DLLs übernommen, die die Mod schon hat. Bei Thunderstore-Paketen werden die Dateien des Mod-Ordners ersetzt, nichts außerhalb davon.
5. Von jeder ersetzten DLL bleibt die alte als `<Name>.dll.old` liegen, falls du zurückwillst.

Ist das Netz weg, passiert nichts, und es wird 10 Minuten lang nicht erneut versucht. Ein Problem im Updater hält das Spiel nie vom Starten ab. Der Updater aktualisiert auch sich selbst, diese Version wird aber erst beim nächsten Start aktiv.

## Installation

Du brauchst BepInEx 5 (für Valheim: [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)). Das Zip `BifrostUpdater-<version>.zip` gibt es auf der [Release-Seite](https://github.com/Stanni97/bifrost-updater/releases/latest).

### Mod-Manager (Windows und Linux)

In [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) oder [Gale](https://thunderstore.io/c/valheim/p/Kesomannen/GaleModManager/) das Zip über **Settings → Import local mod** einbinden. Ändere nichts weiter.

### Manuell unter Windows

`BifrostUpdater.dll` aus dem Ordner `patchers` des Zips nach `BepInEx\patchers\BifrostUpdater\` im Spielordner kopieren (Steam: Rechtsklick auf Valheim → **Verwalten** → **Lokale Dateien durchsuchen**). Spiel wie gewohnt starten.

### Manuell unter Linux

Dasselbe nach `~/.local/share/Steam/steamapps/common/Valheim/BepInEx/patchers/BifrostUpdater/` kopieren. BepInEx muss wie gewohnt eingerichtet sein (`./start_game_bepinex.sh %command%` als Startoption).

### Proton und Server

- **Proton:** wie unter Windows installieren, mit der Startoption, die BepInEx ohnehin braucht.
- **Dedizierter Server:** dieselbe Datei im `BepInEx/patchers`-Ordner des Servers. Beachte beim Server das Kapitel zu `ThirdParty = Update` unten.

### Funktioniert es?

In `BepInEx/LogOutput.log` stehen Zeilen mit `BifrostUpdater`, z. B. `Updating KongStrong 1.1.0.0 -> 1.2.0.0 ...` oder `Update available: ...`. Ausführlichere Meldungen kommen mit dem Log-Level Debug.

## Konfiguration

Es ist keine Konfiguration nötig. Nach dem ersten Start liegt `BepInEx/config/bifrost.updater.cfg` mit den Standardwerten und Erklärungen bereit:

| Schlüssel | Bedeutung |
| --- | --- |
| `Enabled` | `false` schaltet den Updater aus |
| `CheckIntervalHours` | Mindestabstand zwischen zwei Prüfungen, `0` = bei jedem Start |
| `TimeoutSeconds` | Wie lange eine Anfrage dauern darf |
| `Trusted` | GitHub-Nutzer und Thunderstore-Teams (oder einzelne `Owner/Repo`, `Owner-Name`), deren Mods ohne Rückfrage aktualisiert werden. Standard: der Besitzer des Updaters |
| `ThirdParty` | Was bei Updates aller anderen Mods passiert: `Off` (ignorieren), `Notify` (nur ins Log schreiben, Standard) oder `Update` (installieren) |
| `RequireChecksum` | GitHub-Downloads nur installieren, wenn die SHA-256-Prüfsumme zu GitHubs Angabe passt (Standard `true`) |
| `Disable` | Mods, die nie angefasst werden, kommagetrennt: Ordnername, `Owner/Repo` oder `Owner-Name` |
| `Mod` | Optional, mehrfach möglich: `Owner/Repo:Datei.dll` legt die Quelle einer Mod fest |

**Vorsicht mit `ThirdParty = Update` im Multiplayer:** Die meisten Mods brauchen auf Client und Server dieselbe Version. Aktualisiert nur dein Client, kommst du nicht mehr auf den Server. Der Updater prüft auch keine Abhängigkeiten, ein Update kann also eine neuere Version von Jotunn oder BepInEx verlangen.

## Eigene Mods anschließen

Damit eine Mod von dir sicher erkannt wird, trägst du in ihre `.csproj` eine Zeile ein:

```xml
<ItemGroup>
  <AssemblyMetadata Include="UpdateRepo" Value="Owner/mein-repo" />
</ItemGroup>
```

Ohne diese Zeile klappt es oft trotzdem, siehe Punkt 4 oben. Damit ein Update ankommt, muss das neueste GitHub-Release:

- einen Tag wie `v1.2.0` haben,
- ein Zip enthalten, in dem die DLL der Mod liegt (egal in welchem Unterordner),
- und die `<Version>` der Mod muss zum Tag passen. Der Updater vergleicht sie mit der Version der installierten DLL.

## Sicherheit

Der Updater lädt Code aus dem Internet und führt ihn im Spiel aus. Deshalb gilt:

- Es wird nur per HTTPS von `github.com`, `api.github.com` und `thunderstore.io` geladen. GitHub-Downloads müssen aus dem Repository der Mod stammen.
- Automatisch wird nur bei Mods vertrauter Besitzer aktualisiert. Fremde Mods meldet er nur.
- Bei GitHub muss die Prüfsumme zu GitHubs Angabe passen. Das schützt vor kaputten oder abgebrochenen Downloads. Wer Zugriff auf das GitHub-Konto hat, könnte aber Release und Prüfsumme gemeinsam austauschen. Thunderstore liefert keine Prüfsumme, dort schützt nur HTTPS.
- Pakete werden vor dem Schreiben vollständig geprüft: keine Pfade aus dem Mod-Ordner heraus, Größenlimits, mindestens eine DLL. Ein kaputtes Zip ändert nichts.
- Wer das nicht möchte, setzt `Enabled = false` oder installiert den Updater gar nicht.

## Selbst bauen

Du brauchst das [.NET SDK](https://dotnet.microsoft.com/download) 8 oder neuer und Valheim mit BepInExPack. Der Build nutzt die DLLs aus dem Spielordner.

```bash
git clone https://github.com/Stanni97/bifrost-updater.git
cd bifrost-updater
dotnet build -c Release
```

Das kopiert `BifrostUpdater.dll` nach `BepInEx/patchers/BifrostUpdater/` und legt das Zip unter `dist/` ab. Mit `-p:InstallToGame=false` wird nur gebaut, mit `-p:ValheimDir="..."` ein anderer Spielordner benutzt (Windows z. B. `-p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim"`).

Wer den Updater für ein eigenes GitHub-Konto baut, ändert den Wert von `UpdateRepo` in der `BifrostUpdater.csproj`. Dieser Besitzer ist danach der Standard für `Trusted`.

## Lizenz

[MIT](LICENSE)
