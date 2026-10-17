# Changelog

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
