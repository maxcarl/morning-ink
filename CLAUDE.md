# CLAUDE.md – Morning Ink

Morning Ink ist ein selbst gehosteter Dienst, der jeden Morgen aus RSS-Feeds und anderen Quellen eine persönliche Zeitung baut (EPUB) und sie an Kindle und andere E-Reader liefert. Er läuft als ein einziger Docker-Container, mit Weboberfläche und optionaler KI.

Das vollständige Konzept steht in `docs/konzept.md`. Lies den passenden Abschnitt, bevor du an einem Bereich arbeitest. Die Aufgaben des MVP stehen dort am Ende unter „MVP-Aufgaben“.

## Tech-Stack

- .NET 10 (LTS), C#, ASP.NET Core, Blazor Web App
- EF Core mit SQLite (Postgres optional später)
- Quartz.NET für Zeitpläne
- SmartReader (Volltext), AngleSharp und HtmlSanitizer (HTML), SkiaSharp (Bilder)
- MailKit (SMTP/IMAP), eigener EPUB-Writer
- Microsoft.Extensions.AI für KI-Anbieter (Ollama, OpenAI-kompatibel, Anthropic)
- Tests: xUnit, bUnit (interaktive Seiten), Testcontainers, NetArchTest
- Docker-Image für amd64 und arm64

## Befehle

```bash
dotnet build                                   # muss ohne Warnungen durchlaufen
dotnet test                                    # alle Tests
dotnet format --verify-no-changes              # Formatierung prüfen (läuft auch in der CI)
dotnet run --project src/MorningInk.Web        # lokal starten
docker compose up --build                      # als Container starten
```

## Projektstruktur

```
src/
  MorningInk.Domain/          # Modelle, Regeln, Value Objects – keine Abhängigkeiten
  MorningInk.Application/     # Anwendungsfälle, Schnittstellen (ISource, IProcessor, IRenderer, IDelivery)
  MorningInk.Infrastructure/  # EF Core, HTTP, Mail, KI, Dateisystem, Implementierungen der Schnittstellen
  MorningInk.Web/             # Blazor-UI, API, Composition Root (DI-Registrierung)
tests/
  MorningInk.Domain.Tests/
  MorningInk.Application.Tests/
  MorningInk.Infrastructure.Tests/   # Integrationstests, gespeicherte Beispielseiten unter TestData/
  MorningInk.Architecture.Tests/     # prüft die Schichtgrenzen
  MorningInk.Web.Tests/              # startet die App im Speicher: Login, Einrichtung, keine Seite ohne Login
docs/
  konzept.md
  adr/                        # Architektur-Entscheidungen
```

## Architekturregeln

- Clean Architecture: Abhängigkeiten zeigen nur nach innen. `Domain` kennt nichts, `Application` kennt nur `Domain`, `Infrastructure` und `Web` kennen beide.
- `Domain` und `Application` referenzieren weder EF Core noch ASP.NET noch HTTP-Bibliotheken.
- Erweiterungen laufen über Schnittstellen: Eine neue Quelle ist eine neue Klasse, die `ISource` implementiert, plus Registrierung. Bestehender Code wird dafür nicht umgebaut.
- Die Architekturtests müssen grün bleiben. Schwäche sie nie ab, um etwas zum Laufen zu bringen.
- Das Datenmodell ist von Anfang an mehrbenutzerfähig: Profile, Quellen und Zugänge gehören immer einem Benutzer.

## Code-Regeln

- Code, Bezeichner, Kommentare und Commit-Messages auf Englisch. Texte in der Oberfläche über Ressourcendateien (Deutsch und Englisch).
- Nullable ist aktiviert, Warnungen gelten als Fehler. Keine `!`-Unterdrückung ohne Kommentar, warum es sicher ist.
- Kleine Klassen mit einer Aufgabe, sprechende Namen, keine magischen Strings oder Zahlen.
- Dependency Injection überall. Einstellungen über das Options-Pattern.
- `TimeProvider` statt `DateTime.Now` oder `DateTimeOffset.UtcNow`, damit Zeitpläne testbar sind.
- HTTP nur über `IHttpClientFactory` und den sicheren Client (siehe Sicherheit), nie `new HttpClient()`.
- Erwartbare Fehler (Quelle offline, Login abgelaufen, Feed ungültig) als Ergebnis-Typ zurückgeben, nicht als Exception. Exceptions nur für echte Programmfehler.
- Strukturiertes Logging mit Platzhaltern, nie String-Interpolation im Log-Aufruf.
- `async` durchgehend, `CancellationToken` an jede asynchrone Methode weiterreichen.

## Sicherheitsregeln (nicht verhandelbar)

Alles von außen ist nicht vertrauenswürdig: URLs, Feeds, Webseiten, Mails, KI-Antworten, Benutzereingaben.

- **Abruf:** Nur `http` und `https`. Interne Adressen (localhost, 10.x, 172.16–31.x, 192.168.x, Link-Local, IPv6-Äquivalente) sind gesperrt, geprüft nach DNS-Auflösung und nach jeder Weiterleitung. Ausnahmen nur über die explizite Allowlist. Maximal 5 Weiterleitungen, Größenlimit, Timeouts.
- **Zugangsdaten:** Nur an die eigene Domain senden. Bei Domainwechsel durch Weiterleitung fallen Header und Cookies weg. Gespeichert werden sie nur verschlüsselt (Data Protection), sie erscheinen nie in Logs, Exporten oder Fehlermeldungen.
- **XML:** Externe Entities und DTD-Verarbeitung aus.
- **HTML:** Vor EPUB und Web-Ansicht über eine strikte Allowlist bereinigen. Kein Script, keine Event-Handler, kein `style` mit URLs.
- **Bilder:** Pixel- und Größenlimit prüfen, bevor dekodiert wird, immer neu kodieren.
- **Dateien:** Dateinamen nie aus Fremdinhalt übernehmen, sondern aus Hash oder ID bilden. Alle Pfade bleiben unter dem Datenverzeichnis.
- **Datenbank:** Nur EF Core bzw. parametrisierte Abfragen. Kein zusammengesetztes SQL.
- **E-Mail:** Betreff und Dateinamen ohne Zeilenumbrüche.
- **Web:** Jede Seite und jede Datei braucht Login. CSRF-Schutz für Formulare. Content-Security-Policy, Artikelvorschau in einer Sandbox-iframe.
- **Externe Programme:** Argumente als Liste, nie über eine Shell.
- **Feedback-Links im EPUB:** signiert (HMAC) und mit Ablaufdatum.

## KI-Regeln

- Artikeltext geht klar abgegrenzt als Daten an das Modell, nie als Teil der Anweisung.
- Die KI bekommt keine Werkzeuge mit Nebenwirkungen. Ihre Antworten werden gegen ein Schema geprüft und können nie Einstellungen ändern.
- Quellen mit „nur lokale KI“ gehen nie an einen Cloud-Anbieter.
- Ohne erreichbare KI muss die Ausgabe trotzdem erscheinen, nur ohne KI-Extras.
- Standard-Prompts liegen versioniert im Repo. Vom Benutzer angepasste Prompts werden bei Updates nie überschrieben.

## Tests

- Schreib zuerst die Tests aus dem Abnahmekriterium der Aufgabe, dann die Umsetzung.
- Unit-Tests für `Domain` und `Application`, Integrationstests für `Infrastructure`.
- Keine echten Netzwerkaufrufe in Tests. Gespeicherte Feeds und Seiten liegen unter `TestData/`.
- Jedes erzeugte EPUB wird in der CI mit epubcheck geprüft.
- Sicherheitsregeln bekommen eigene Tests, z. B. für Weiterleitungen ins Heimnetz und für `file:`-URLs.

## Arbeitsweise

- Eine Aufgabe (ein Issue) pro Sitzung. Fass keinen Code an, der nicht zur Aufgabe gehört.
- Neue NuGet-Pakete nur nach Rückfrage. Lizenz prüfen und im PR nennen.
- Größere Entscheidungen als kurze ADR unter `docs/adr/` festhalten.
- Kleine Commits mit Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`, `chore:`).
- Wenn das Konzept und eine Aufgabe sich widersprechen oder etwas unklar ist: nachfragen, nicht raten.
- Ändert sich durch die Arbeit etwas am Konzept, schlag die Änderung an `docs/konzept.md` mit vor.

## Definition of Done

- Das „Fertig, wenn“ der Aufgabe ist erfüllt und durch Tests belegt.
- `dotnet build` ohne Warnungen, `dotnet test` grün, `dotnet format` ohne Änderungen.
- Architekturtests grün.
- Keine Geheimnisse, Zugangsdaten oder persönlichen Daten im Code, in Tests oder in Logs.
- Neue Einstellungen sind dokumentiert, neue UI-Texte stehen in beiden Ressourcendateien.
