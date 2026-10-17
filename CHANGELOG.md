# Changelog

## 1.0.0

- Erste Version: findet die installierten Mods automatisch und prüft beim Spielstart ihre neuesten Versionen.
- Mods von Stanni97 (Update-Quelle in den Assembly-Metadaten) werden aus GitHub-Releases aktualisiert, mit SHA-256-Prüfung.
- Mods aus Thunderstore (manifest.json) werden gemeldet oder auf Wunsch aktualisiert (`ThirdParty = Notify | Update | Off`).
- Der Updater aktualisiert auch sich selbst.
