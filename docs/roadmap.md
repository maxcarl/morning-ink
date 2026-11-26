# Roadmap

What Morning Ink does today is in the [README](../README.md#features). This page lists what could come next. Nothing here is promised; ideas move up when someone needs them. Open an issue to discuss one before starting on it.

## Sources

| Source | Idea |
| --- | --- |
| JSON Feed | Next to RSS and Atom |
| Website without a feed | Scraper with CSS selectors for list, title and link |
| Miniflux | Like FreshRSS: categories, unread only, mark read afterwards |
| Read-it-later | Wallabag, Readeck: saved articles go into the next issue, like Karakeep |
| Lobsters | Next to Hacker News and Reddit, with a minimum score |
| Daily tiles | Calendar (CalDAV/ICS), waste collection, stock prices: compact on the cover |
| Smart home | Home Assistant sensor values or notices such as "window open" |
| Read tomorrow | Share target in the installed app, browser extension, Telegram bot: send a link, it lands in the next issue |
| API / webhook | Feed any content by POST from your own scripts |

**Logins for sources:** OAuth 2 with automatic token refresh (Wallabag, Reddit, Mastodon, Google Calendar), form login for newspaper subscriptions (login URL, fields, success check, reused session), cookie import for sites with 2FA or captcha (with expiry warning). Each login gets a "test login" button and a status; an expired login sends a notification instead of producing empty articles.

## Processing

- Rules per website when the automatic full-text extraction misses
- Images: dithering for e-ink, a maximum number per article
- Filters: language
- Translation of foreign-language sources by AI, DeepL or LibreTranslate, optionally bilingual

## AI

| Feature | Idea |
| --- | --- |
| Weekly review | A Sunday issue with the week's big stories |
| Kids' edition | A profile where the AI explains selected news for children; age, topics and exclusions adjustable |
| Model per task | A small local model for bulk work, a stronger cloud model for the overview |
| Native Anthropic provider | Only if the OpenAI-compatible endpoint stops being enough (e.g. for prompt caching) |

Private source types (newsletters, calendar, Home Assistant, sources with a login) should default to "local AI only".

## Issue and layout

- PDF in newspaper layout: two columns, for reMarkable, tablets or printing
- Layout templates (classic, compact, magazine) in a sandboxed template engine; typeface, image size, QR code to the original, section colors for color e-ink
- Reading-time budget: fill the issue by priority up to e.g. 20 minutes; important texts in full, others as summary only
- Links as numbered endnotes, calmer to read on e-ink
- Closing page with odds and ends, a quote or a puzzle (sudoku, crossword)
- Weekend longread issue with the week's longer articles

## Delivery and schedule

- Storage: WebDAV, Nextcloud or a folder on the server
- Messenger delivery: Telegram or Matrix bot sends the file
- Cron expressions next to time and weekdays
- Trigger an issue by API or a Home Assistant button
- Shrink images harder when an issue would exceed the mail limit

## Web UI

- Archive as a stack of cover thumbnails
- Sign-in with OIDC (Authentik, Authelia, Keycloak) or header auth behind a reverse proxy
- Change a user's role
- Template gallery: ready-made bundles such as "Tech morning" to import
- More UI languages (resource files)

## Operations

- PostgreSQL as an alternative to SQLite
- Status page: warn when the data directory grows past a set size or the volume runs low; `/health` also checks the scheduler and free space
- Backups on a schedule (download and restore from the status page exist)
- Export and import of the whole configuration as JSON on the status page, to keep it in Git or move it; secrets left out or from environment variables or Docker secrets
- REST API and webhooks, Home Assistant integration
- Sync read state back to FreshRSS or Miniflux