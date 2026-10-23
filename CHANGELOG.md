# Changelog

## 1.2.0

- Neu: Dateien vom eigenen Spielserver (`FileSource`). Ein Server kann Config-Dateien anbieten, die eine Mod braucht, aber nicht mitliefert (z. B. den Ruf von Kong Strong). Der Updater installiert sie nach `BepInEx/config`, wenn der Spieler sie noch nicht hat.
- Eine eigene Datei des Spielers wird nie ersetzt. Eine Datei, die der Updater selbst installiert hat und die seitdem unverändert ist, folgt Änderungen auf dem Server.
- Sicherheit: Jede Datei wird mit der SHA-256-Prüfsumme aus der Liste des Servers verglichen. Es werden nur Dateien unter `BepInEx/config` geschrieben und nie Programme (`.dll`, `.exe`, Skripte), auch weil die Quelle reines http sein darf.
- Mehrere Adressen sind möglich (z. B. LAN und VPN), die erste erreichbare zählt. Eine nicht erreichbare Quelle kostet höchstens 3 Sekunden.

## 1.1.0

- Dedizierte Server werden erkannt (`valheim_server` oder `-batchmode -nographics`). Dort gilt der neue Schlüssel `ServerCheckIntervalHours`, Standard `0`: Der Server prüft bei jedem Start und startet so mit den Versionen, die die Updater der Spieler gerade installiert haben.
- Neuer Schlüssel `MirrorTo` (Standard `auto`): Server-Images wie lloesche/valheim-server kopieren bei jedem Start `/config/bepinex/plugins` und `patchers` ins Spiel. Ein Update landet jetzt auch dort, sonst brachte der nächste Start die alte Version zurück. `off` schaltet das ab, ein Pfad legt den Ordner fest.
- Die Meldung nach einem Update sagt richtig, ab wann die neue Version läuft: Plugins schon im selben Start, Patcher erst beim nächsten.

## 1.0.2

- Windows: Wenn die eingebaute Verbindung scheitert, lädt der Updater jetzt mit `curl.exe` und danach mit PowerShell (TLS 1.2). Linux nutzt `curl` und danach `wget`.
- Eine Antwort des Servers mit Fehlercode (z. B. 404) gilt als „online“. Dadurch bricht eine nicht gefundene Mod nicht mehr die Prüfung aller anderen ab.
- Leere Downloads und fehlende Programme werden mit kurzer Meldung abgefangen.
- Zips mit Backslashes im Pfad (unter Windows gebaut) werden unter Linux richtig gelesen.
- Die Config-Datei bekommt unter Windows Windows-Zeilenenden.

## 1.0.1

- Behoben: Unter Linux mit Steam konnte der Updater keine Verbindung aufbauen, weil das Steam-Laufzeitumfeld `curl` kaputtmacht. `curl` und der neue Rückfall `wget` laufen jetzt mit bereinigter Umgebung.
- Wenn der Download nicht klappt, steht der Grund beider Wege im Log.
- Nach einem Verbindungsfehler wird nach 10 statt 30 Minuten erneut versucht.

## 1.0.0

- Erste Version (vorher StanniUpdater): findet die installierten Mods automatisch und prüft beim Spielstart ihre neuesten Versionen. Windows und Linux, ohne Einstellungen.
- Quellen: Mod-Config, Update-Quelle in der DLL (`UpdateRepo`), Thunderstore-`manifest.json`, passende Repositories der vertrauten GitHub-Nutzer.
- Mods vertrauter Besitzer werden aus GitHub-Releases aktualisiert, mit SHA-256-Prüfung. Fremde Mods werden gemeldet oder auf Wunsch aktualisiert (`ThirdParty = Notify | Update | Off`).
- Der Updater aktualisiert auch sich selbst. Alte Einstellungen von StanniUpdater werden übernommen.
