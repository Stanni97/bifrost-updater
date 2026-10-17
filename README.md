# StanniUpdater

Ein kleiner Auto-Updater für die Valheim-Mods von [Stanni97](https://github.com/Stanni97). Beim Start des Spiels fragt er GitHub nach dem neuesten Release jeder installierten Mod und ersetzt die DLL, wenn es eine neuere Version gibt.

Gepflegt werden zurzeit:

| Mod | Repository |
| --- | --- |
| Kong Strong Energy | [valheim-kong-strong](https://github.com/Stanni97/valheim-kong-strong) |
| Verler Leben | [verler-leben](https://github.com/Stanni97/verler-leben) |
| StanniUpdater selbst | [stanni-updater](https://github.com/Stanni97/stanni-updater) |

## So funktioniert es

Der Updater ist ein **BepInEx-Patcher**. Patcher laufen, bevor BepInEx irgendein Plugin lädt. Eine aktualisierte Mod ist deshalb schon im selben Spielstart die neue Version, und es gibt keine Probleme mit gesperrten Dateien unter Windows. Ein Update beim Schließen des Spiels wäre dagegen unsicher: Unter Windows ist die DLL dann noch gesperrt, und ein Absturz würde das Update verlieren.

Pro Start passiert Folgendes:

1. Für jede bekannte Mod, die installiert ist, wird `releases/latest` des Repositorys abgefragt. Ist eine Mod nicht installiert, passiert nichts. Der Updater installiert nie etwas Neues.
2. Ist die Release-Version (Tag `v1.2.0`) höher als die der installierten DLL, wird das Zip des Releases heruntergeladen.
3. Die SHA-256-Prüfsumme des Downloads wird mit der verglichen, die GitHub für die Datei anzeigt.
4. Aus dem Zip wird **nur die DLL** entnommen (nichts anderes wird je auf die Platte geschrieben) und ersetzt die alte. Die alte bleibt als `<Name>.dll.old` liegen, falls du zurückwillst.

Gefragt wird höchstens alle 6 Stunden (`CheckIntervalHours`). Ist GitHub nicht erreichbar, passiert nichts, und es wird 30 Minuten lang nicht erneut versucht. Spielstart wird dadurch nie verhindert.

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
| `RequireChecksum` | Nur installieren, wenn die SHA-256-Prüfsumme zu GitHubs Angabe passt (Standard `true`) |
| `AllowedOwners` | Nur Repositories dieser GitHub-Nutzer werden angefragt, Standard `Stanni97` |
| `Disable` | Repositories, die nicht aktualisiert werden sollen, kommagetrennt |
| `Mod` | Eine weitere Mod als `Owner/Repo:Datei.dll`, mehrfach möglich. Die Mod muss schon installiert sein |

Eine Mod pausierst du zum Beispiel mit `Disable = Stanni97/verler-leben`.

## Damit eine Mod aktualisiert werden kann

- Das neueste GitHub-Release hat einen Tag wie `v1.2.0`.
- Es enthält ein Zip, in dem die DLL der Mod liegt (egal in welchem Unterordner).
- Die `<Version>` in der `.csproj` der Mod stimmt mit dem Tag überein. Der Updater vergleicht sie mit der Version der installierten DLL.

## Sicherheit

Der Updater lädt Code aus dem Internet und führt ihn im Spiel aus. Deshalb gilt:

- Es wird nur per HTTPS von `github.com` und `api.github.com` geladen, und die Download-Adresse muss im jeweiligen Repository liegen.
- Es werden nur Repositories der `AllowedOwners` angefragt (Standard: nur Stanni97).
- Die Prüfsumme muss zu GitHubs Angabe passen. Das schützt vor kaputten oder abgebrochenen Downloads. Wer Zugriff auf das GitHub-Konto hat, könnte aber Release und Prüfsumme gemeinsam austauschen. Das ist das Vertrauen, das jedes Auto-Update braucht.
- Wer das nicht möchte, setzt `Enabled = false` oder installiert den Updater gar nicht.

## Selbst bauen

Du brauchst das [.NET SDK](https://dotnet.microsoft.com/download) 8 oder neuer und Valheim mit BepInExPack. Der Build nutzt die DLLs aus dem Spielordner.

```bash
git clone https://github.com/Stanni97/stanni-updater.git
cd stanni-updater
dotnet build -c Release
```

Das kopiert `StanniUpdater.dll` nach `BepInEx/patchers/StanniUpdater/` und legt das Zip unter `dist/` ab. Mit `-p:InstallToGame=false` wird nur gebaut, mit `-p:ValheimDir="..."` ein anderer Spielordner benutzt.

Eine weitere Mod fest einbauen: in `src/Settings.cs` einen Eintrag zu `BuiltIn` hinzufügen.

## Lizenz

[MIT](LICENSE)
