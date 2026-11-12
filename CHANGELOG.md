# Changelog

Alle nennenswerten Änderungen, neueste zuerst. Neue Einträge kommen unter „Unveröffentlicht“, beim Release macht pinx daraus die neue Version.

## [Unveröffentlicht]

## [0.3.0] – 2026-11-12

- Oberfläche im Stil von coss ui überarbeitet: Werkzeugleiste mit Pfad und Speicherstatus, Ansicht-Umschalter (Beide / Markdown / Vorschau), Textebene-Auswahl, Format-Symbolen, Diktat-Knopf mit Laufzeit und Pegel und Suche; Seitenleiste mit Tabs, Symbolen und „Neu“/„Öffnen …“; Kontextmenü mit Schrift-Symbolreihe und Gruppen; Statusleiste mit Theme-Auswahl; Einzelansichten auf Lesebreite zentriert. Minimal Light/Dark mit neutralen coss-Farben; alle Funktionen und Tastenkürzel bleiben

## [0.2.0] – 2026-10-24

- Update-Suche: Der Editor prüft beim Start und über Hilfe → „Nach Updates suchen…“ auf ein neues GitHub-Release und installiert es auf Wunsch

## [0.1.0] – 2026-10-23

- projekt.ini: Kommentare auf neuen Namen pinx (früher Projekt-Cockpit)
- Kategorie Programme in projekt.ini eingetragen
- Aufgaben per Klick, Bilder per Strg+V/Drag & Drop, Link- und Tabellenaktionen im Rechtsklick
- KI-Funktionen entfernt, Format-/Kontextmenü in Gruppen neu aufgebaut
- Dateizuordnung: Skript registriert den Editor für Textformate und entfernt veraltete Einträge
- projekt.ini: Start ueber start.ps1 (holt Updates, baut bei Bedarf neu)
- Desktop-Verknüpfung: projekt.ini (Name, Startweg) und App-Icon
- start.ps1: holt Updates von GitHub, baut bei neuerem Quellcode neu und startet (Ziel der Desktop-Verknuepfung)
- Erster Stand
