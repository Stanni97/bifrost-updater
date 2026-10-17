# Changelog

## 1.0.0

- Erste Version (vorher StanniUpdater): findet die installierten Mods automatisch und prüft beim Spielstart ihre neuesten Versionen. Windows und Linux, ohne Einstellungen.
- Quellen: Mod-Config, Update-Quelle in der DLL (`UpdateRepo`), Thunderstore-`manifest.json`, passende Repositories der vertrauten GitHub-Nutzer.
- Mods vertrauter Besitzer werden aus GitHub-Releases aktualisiert, mit SHA-256-Prüfung. Fremde Mods werden gemeldet oder auf Wunsch aktualisiert (`ThirdParty = Notify | Update | Off`).
- Der Updater aktualisiert auch sich selbst. Alte Einstellungen von StanniUpdater werden übernommen.
