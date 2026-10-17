# StanniUpdater

Ein wartungsarmer Auto-Updater für BepInEx-Mods in Valheim. Beim Start des Spiels findet er die installierten Mods selbst, fragt nach neueren Versionen und ersetzt die Dateien. Es gibt keine Liste, die du pflegen musst. Gebaut für die Mods von [Stanni97](https://github.com/Stanni97), er kann aber auch Mods von Thunderstore im Blick behalten.

## So funktioniert es

Der Updater ist ein **BepInEx-Patcher**. Patcher laufen, bevor BepInEx irgendein Plugin lädt. Eine aktualisierte Mod ist deshalb schon im selben Spielstart die neue Version, und es gibt keine Probleme mit gesperrten Dateien unter Windows. Ein Update beim Schließen des Spiels wäre dagegen unsicher: Unter Windows ist die DLL dann noch gesperrt, und ein Absturz würde das Update verlieren.

**Mods werden automatisch erkannt**, auf zwei Wegen:

1. **Eingebaute Quelle:** Eine DLL nennt ihr eigenes Repository in den Assembly-Metadaten (`AssemblyMetadata("UpdateRepo", "Stanni97/verler-leben")`). Das ist der Weg für alle Mods von Stanni97. Die Updates kommen aus dem neuesten GitHub-Release.
2. **Thunderstore:** Mods, die über r2modman oder Gale installiert wurden, liegen in einem Ordner `Namespace-Name` mit `manifest.json`. Daraus liest der Updater Name und Version und fragt die Thunderstore-Schnittstelle nach der neuesten Version.

Pro Start passiert Folgendes:

1. Alle Mods in `BepInEx/plugins` und `BepInEx/patchers` werden erkannt. Neue Mods brauchen keine Eintragung.
2. Pro Mod wird die neueste Version abgefragt (höchstens alle 6 Stunden, `CheckIntervalHours`).
3. Ist sie neuer, hängt es vom Besitzer ab: Mods von **vertrauten Besitzern** (Standard: Stanni97) werden aktualisiert. Bei allen anderen schreibt der Updater nur eine Zeile ins Log (`ThirdParty = Notify`), außer du stellst `ThirdParty = Update` ein.
4. Bei GitHub-Downloads muss die SHA-256-Prüfsumme zu GitHubs Angabe passen. Aus dem Zip werden nur die DLLs übernommen, die die Mod schon hat. Bei Thunderstore-Paketen werden die Dateien des Mod-Ordners ersetzt, nichts außerhalb davon.
5. Von jeder ersetzten DLL bleibt die alte als `<Name>.dll.old` liegen, falls du zurückwillst.

Ist das Netz weg, passiert nichts, und es wird 30 Minuten lang nicht erneut versucht. Ein Problem im Updater hält das Spiel nie vom Starten ab.

Der Updater aktualisiert auch sich selbst. Diese Version wird allerdings erst beim nächsten Start aktiv.

## Installation

Du brauchst [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Das Zip `StanniUpdater-<version>.zip` gibt es auf der [Release-Seite](https://github.com/Stanni97/stanni-updater/releases/latest).

**Mod-Manager (r2modman, Gale):** das Zip über **Settings → Import local mod** einbinden.

**Windows manuell:** `StanniUpdater.dll` aus dem Ordner `patchers` des Zips nach `BepInEx\patchers\StanniUpdater\` kopieren.

**Linux manuell:** dasselbe nach `~/.local/share/Steam/steamapps/common/Valheim/BepInEx/patchers/StanniUpdater/`.

Nach dem ersten Start liegt die Config unter `BepInEx/config/stanni.updater.cfg`. In `BepInEx/LogOutput.log` steht, was der Updater getan hat, z. B. `Updating KongStrong.dll 1.1.0.0 -> 1.2.0.0 ...`.

## Konfiguration

`BepInEx/config/stanni.updater.cfg`, eine Zeile pro Einstellung:

| Schlüssel | Bedeutung |
| --- | --- |
| `Enabled` | `false` schaltet den Updater aus |
| `CheckIntervalHours` | Mindestabstand zwischen zwei Prüfungen, `0` = bei jedem Start |
| `TimeoutSeconds` | Wie lange eine Anfrage dauern darf |
| `TrustedOwners` | GitHub-Nutzer und Thunderstore-Teams, deren Mods ohne Rückfrage aktualisiert werden, Standard `Stanni97` |
| `ThirdParty` | Was bei Updates aller anderen Mods passiert: `Off` (ignorieren), `Notify` (nur ins Log schreiben, Standard) oder `Update` (installieren) |
| `RequireChecksum` | GitHub-Downloads nur installieren, wenn die SHA-256-Prüfsumme zu GitHubs Angabe passt (Standard `true`) |
| `Disable` | Mods, die nie angefasst werden, kommagetrennt: Ordnername, `Owner/Repo` oder `Owner-Name` |

**Vorsicht mit `ThirdParty = Update` im Multiplayer:** Die meisten Mods brauchen auf Client und Server dieselbe Version. Aktualisiert nur dein Client, kommst du nicht mehr auf den Server. Der Updater prüft auch keine Abhängigkeiten, ein Update kann also eine neuere Version von Jotunn oder BepInEx verlangen.

## Eine neue Mod anschließen

Für Mods von Stanni97 reicht eine Zeile in der `.csproj`, danach ist alles automatisch:

```xml
<ItemGroup>
  <AssemblyMetadata Include="UpdateRepo" Value="Stanni97/mein-repo" />
</ItemGroup>
```

Damit ein Update ankommt, muss das neueste GitHub-Release einen Tag wie `v1.2.0` haben, ein Zip mit der DLL enthalten, und die `<Version>` in der `.csproj` muss zum Tag passen. Kong Strong Energy und Verler Leben in den Versionen vor dem Updater tragen die Zeile noch nicht, sie sind deshalb fest hinterlegt (nur diese zwei).

## Sicherheit

Der Updater lädt Code aus dem Internet und führt ihn im Spiel aus. Deshalb gilt:

- Es wird nur per HTTPS von `github.com`, `api.github.com` und `thunderstore.io` geladen. GitHub-Downloads müssen aus dem Repository der Mod stammen.
- Ohne Einstellung wird nur bei Mods von `TrustedOwners` automatisch aktualisiert. Fremde Mods meldet er nur.
- Bei GitHub muss die Prüfsumme zu GitHubs Angabe passen. Das schützt vor kaputten oder abgebrochenen Downloads. Wer Zugriff auf das GitHub-Konto hat, könnte aber Release und Prüfsumme gemeinsam austauschen. Thunderstore liefert keine Prüfsumme, dort schützt nur HTTPS.
- Pakete werden vor dem Schreiben vollständig geprüft: keine Pfade aus dem Mod-Ordner heraus, Größenlimits, mindestens eine DLL. Ein kaputtes Zip ändert nichts.
- Wer das nicht möchte, setzt `Enabled = false` oder installiert den Updater gar nicht.

## Selbst bauen

Du brauchst das [.NET SDK](https://dotnet.microsoft.com/download) 8 oder neuer und Valheim mit BepInExPack. Der Build nutzt die DLLs aus dem Spielordner.

```bash
git clone https://github.com/Stanni97/stanni-updater.git
cd stanni-updater
dotnet build -c Release
```

Das kopiert `StanniUpdater.dll` nach `BepInEx/patchers/StanniUpdater/` und legt das Zip unter `dist/` ab. Mit `-p:InstallToGame=false` wird nur gebaut, mit `-p:ValheimDir="..."` ein anderer Spielordner benutzt.

## Lizenz

[MIT](LICENSE)
