# Morning Ink

**Your personal morning newspaper, delivered to your e-reader.**

[![CI](https://github.com/maxcarl/morning-ink/actions/workflows/ci.yml/badge.svg)](https://github.com/maxcarl/morning-ink/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Container](https://img.shields.io/badge/ghcr.io-amd64%20%7C%20arm64-2496ED?logo=docker&logoColor=white)](https://github.com/maxcarl/morning-ink/pkgs/container/morning-ink)

A self-hosted service that turns your RSS feeds into a daily EPUB newspaper and sends it to your Kindle. One Docker container, web UI, optional AI.

> [!WARNING]
> Morning Ink is in early development and **not usable yet**. See the [roadmap](#roadmap).

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

Set at least these environment variables, e.g. in `compose.yaml`:

| Setting | Meaning |
| --- | --- |
| `Smtp__Host`, `Smtp__Username`, `Smtp__Password` | Your outgoing mail server |
| `Smtp__FromAddress` | Sender; add it to the approved list in your Amazon account |

All settings: [docs/configuration.md](docs/configuration.md). Mail settings will also be editable in the web UI.

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
