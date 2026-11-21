# Kindle-Checkliste

Manuelle Prüfung einer Ausgabe auf einem echten Kindle. epubcheck in der CI prüft die Form, diese Liste prüft, was Amazons Umwandlung und das Gerät daraus machen.

Testdatei: eine Ausgabe per Send to Kindle (Mail oder https://www.amazon.de/sendtokindle) an das Gerät schicken.

| Punkt | Erwartung | Ergebnis |
| --- | --- | --- |
| Zustellung | Kommt ohne Fehlermail von Amazon an | ✅ 2026-11-21 |
| Titel | „Morning Ink JJJJ-MM-TT“, Ausgaben sortieren sich nach Datum | |
| Cover | Bibliothek zeigt das Cover mit Titel, Datum und Schlagzeilen | ✅ 2026-11-21 |
| Inhaltsverzeichnis | „Gehe zu“ zeigt Titelseite, Rubriken und darunter die Artikel | ✅ 2026-11-21 |
| Navigation | Sprung aus dem Inhaltsverzeichnis und von der Titelseite landet beim richtigen Artikel | ✅ 2026-11-21 |
| Umlaute | ä, ö, ü, ß und typografische Anführungszeichen erscheinen korrekt | |
| Bilder | Graustufen, volle Breite, nicht abgeschnitten | |
| Links | „Zum Original“ öffnet die Seite im Kindle-Browser | |
| Hinweise | Ausfallvermerke stehen auf der Titelseite | |
| Lesbarkeit | Schriftgröße und Schriftart lassen sich am Gerät ändern | |
