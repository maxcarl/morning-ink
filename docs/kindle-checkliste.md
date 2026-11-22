# Kindle-Checkliste

Manuelle Prüfung einer Ausgabe auf einem echten Kindle. epubcheck in der CI prüft die Form, diese Liste prüft, was Amazons Umwandlung und das Gerät daraus machen.

Testdatei: eine Ausgabe per Send to Kindle (Mail oder https://www.amazon.de/sendtokindle) an das Gerät schicken. Für den Weg per Mail: `tools/send-test-issue.cs` (Anleitung im Kopf der Datei).

Stand 2026-11-22: alles außer dem Titel per Web-Upload geprüft. Zustellung per Mail (Gmail, Testmail aus den Einstellungen) am 2026-11-22 geprüft; der Titel per Mail steht noch aus.

| Punkt | Erwartung | Ergebnis |
| --- | --- | --- |
| Zustellung | Kommt ohne Fehlermail von Amazon an | ✅ 2026-11-21 (Web-Upload), ✅ 2026-11-22 (Mail über Gmail) |
| Titel | „Morning Ink JJJJ-MM-TT“, Ausgaben sortieren sich nach Datum | ⚠️ 2026-11-21: Upload per Web-Oberfläche zeigt den Dateinamen. Deshalb trägt der Mail-Anhang den Titel als Dateinamen (Aufgabe 12); dort erneut prüfen |
| Cover | Bibliothek zeigt das Cover mit Titel, Datum und Schlagzeilen | ✅ 2026-11-21 |
| Inhaltsverzeichnis | „Gehe zu“ zeigt Titelseite, Rubriken und darunter die Artikel | ✅ 2026-11-21 |
| Navigation | Sprung aus dem Inhaltsverzeichnis und von der Titelseite landet beim richtigen Artikel | ✅ 2026-11-21 |
| Umlaute | ä, ö, ü, ß und typografische Anführungszeichen erscheinen korrekt | ✅ 2026-11-21 |
| Bilder | Graustufen, volle Breite, nicht abgeschnitten | ✅ 2026-11-21 |
| Links | „Zum Original“ öffnet die Seite im Kindle-Browser | ✅ 2026-11-21 |
| Hinweise | Ausfallvermerke stehen auf der Titelseite | ✅ 2026-11-21 |
| Lesbarkeit | Schriftgröße und Schriftart lassen sich am Gerät ändern | |
