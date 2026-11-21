# Morning Ink

**Your personal morning newspaper, delivered to your e-reader.**

[![CI](https://github.com/maxcarl/morning-ink/actions/workflows/ci.yml/badge.svg)](https://github.com/maxcarl/morning-ink/actions/workflows/ci.yml)

Morning Ink is a self-hosted service that collects articles from your RSS feeds and other sources every morning, turns them into a clean EPUB newspaper and sends it to your Kindle or another e-reader. It runs as a single Docker container with a web UI, and AI features are optional.

> [!WARNING]
> Morning Ink is in early development and is **not usable yet**. The project skeleton, container and CI are in place; fetching, rendering and delivery are being built now. See the [roadmap](#roadmap).

## Features

Planned for the first release:

- **RSS and Atom feeds** with full-text extraction and clean, e-ink friendly formatting
- **Local news and weather** for your region, by postal code or town
- **EPUB newspaper** with cover page, table of contents and sections
- **Send to Kindle** by email, on your own schedule and in your time zone
- **Web UI** for sources, schedules and settings, with a setup wizard
- **One container, no required dependencies:** SQLite and a built-in scheduler
- **Safe by default:** blocks requests to your home network, encrypts stored credentials and sanitizes all HTML

Later: more sources (newsletters via IMAP, FreshRSS, Miniflux, Wallabag, YouTube), more readers (Kobo, KOReader, PocketBook, reMarkable via OPDS, WebDAV and PDF), several users, and optional AI summaries with Ollama or a cloud provider. Morning Ink always works without AI.

### Non-goals

- Not a replacement for your RSS reader. Morning Ink connects to FreshRSS and Miniflux instead.
- Not a public service or SaaS. It is meant for you, your family and friends.
- No paywall circumvention. Paid content only with your own subscription and login.

## Getting started

Requires Docker.

```bash
git clone https://github.com/maxcarl/morning-ink.git
cd morning-ink
docker compose up -d
```

The web UI will be available at <http://localhost:8080>. All data lives in the `/data` volume.

Prebuilt images for `amd64` and `arm64` are published to `ghcr.io/maxcarl/morning-ink`.

## Configuration

Settings come from `appsettings.json` or environment variables (`Section__Key`, array entries as `Section__Key__0`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `Storage__DataDirectory` | `/data` | Database, backups and encryption keys. Back up the whole directory: without `keys/`, stored passwords cannot be decrypted. |
| `Http__AllowedHosts__0`, `__1`, … | – | Internal hosts Morning Ink may reach, e.g. `freshrss.lan` or `192.168.1.20:8080`. Everything in your home network is blocked otherwise. |
| `Http__MaxRedirects` | `5` | Redirects followed per request. |
| `Http__MaxResponseBytes` | `10485760` | Largest response accepted (10 MiB). |
| `Http__Timeout` | `00:00:30` | Time for a whole request, including redirects. |
| `Http__ConnectTimeout` | `00:00:10` | Time to open one connection. |
| `Smtp__Host` | – | Outgoing mail server. Without it, issues are not sent. |
| `Smtp__Port` | `587` | Port of the mail server. |
| `Smtp__Security` | `StartTls` | `StartTls` (port 587), `SslOnConnect` (port 465) or `None` (unencrypted, only for a local relay). |
| `Smtp__Username`, `Smtp__Password` | – | Login at the mail server. Pass the password as an environment variable or Docker secret. |
| `Smtp__FromAddress` | – | Sender. Add it to *Approved Personal Document E-mail List* in your Amazon account, or Amazon drops the mail. |
| `Smtp__FromName` | `Morning Ink` | Sender name. |
| `Smtp__MaxMessageBytes` | `52428800` | Largest email including encoding (50 MiB, Amazon's limit). |
| `Images__MinDimension` | `100` | Images narrower or lower than this (icons, avatars, tracking pixels) are dropped. |
| `Images__MaxDimension` | `1000` | Longest side of converted images, in pixels. |
| `Images__MaxPixels` | `40000000` | Larger images are rejected before decoding. |
| `Images__MaxDownloadBytes` | `5242880` | Largest image downloaded (5 MiB). |
| `Images__MaxEncodedBytes` | `204800` | Largest converted JPEG (200 KiB); quality and size go down until it fits. |
| `Images__MaxPerArticle` | `10` | Images kept per article. |
| `Images__Quality` | `75` | JPEG quality of the first attempt. |

## Development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build                          # must build without warnings
dotnet test                           # all tests
dotnet format --verify-no-changes     # formatting check, also run in CI
dotnet run --project src/MorningInk.Web
```

The code follows Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Web`), and architecture tests enforce the layer boundaries. The full design, in German, is in [docs/konzept.md](docs/konzept.md).

## Roadmap

The first release is split into 25 tasks in five stages. Their progress is tracked in the [concept](docs/konzept.md#mvp-aufgaben).

1. **Foundation:** solution, CI, container, database, encrypted credentials
2. **Fetching content:** safe HTTP client, feeds, full text, images, duplicates
3. **Newspaper:** EPUB, Kindle delivery, schedule, local news and weather
4. **Web UI:** login, sources, profiles, settings, dashboard, setup wizard
5. **Acceptance:** documentation and a seven-day trial run

## Contributing

Issues and ideas are welcome. Please open an issue before starting larger changes so we can agree on the approach. Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

## Private use

Full texts, images and archived issues are for your personal use only. Respect the terms of the sites you subscribe to.

## License

Not decided yet.
