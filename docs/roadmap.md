# Roadmap

What Morning Ink does today is in the [README](../README.md#features). This page lists what could come next. Nothing here is promised; ideas move up when someone needs them. Open an issue to discuss one before starting on it.

## First release

- [ ] Seven-day trial run: three real feeds arrive on the Kindle at 6:00 every day without errors
- [ ] Someone without prior knowledge gets from an empty container to the first newspaper in under 10 minutes, using only the README
- [ ] Setup wizard run through with a real Kindle
- [ ] Kindle checklist: font size and typeface changeable on the device; several real issues sort by date ([checklist](kindle-checklist.md))

## Sources

| Source | Idea |
| --- | --- |
| JSON Feed | Next to RSS and Atom |
| Website without a feed | Scraper with CSS selectors for list, title and link |
| Miniflux | Like FreshRSS: categories, unread only, mark read afterwards |
| Read-it-later | Wallabag, Readeck, Karakeep: saved articles go into the next issue |
| Lobsters | Next to Hacker News and Reddit, with a minimum score |
| Social | Mastodon lists, Bluesky |
| Video and audio | YouTube channels and podcasts: transcript or show notes, summarized by AI |
| Daily tiles | Calendar (CalDAV/ICS), waste collection, stock prices: compact on the cover |
| Smart home | Home Assistant sensor values or notices such as "window open" |
| Read tomorrow | Share target in the installed app, browser extension, Telegram bot: send a link, it lands in the next issue |
| API / webhook | Feed any content by POST from your own scripts |
| Odds and ends | Wikipedia article of the day, "on this day": a small section at the end |

**Logins for sources:** OAuth 2 with automatic token refresh (Wallabag, Reddit, Mastodon, Google Calendar), form login for newspaper subscriptions (login URL, fields, success check, reused session), cookie import for sites with 2FA or captcha (with expiry warning). Each login gets a "test login" button and a status; an expired login sends a notification instead of producing empty articles.

## Processing

- Rules per website when the automatic full-text extraction misses
- Images: dithering for e-ink, a maximum number per article
- Filters: regular expressions, language
- Translation of foreign-language sources by AI, DeepL or LibreTranslate, optionally bilingual
- "Missed yesterday": articles that did not fit the limit move to the next issue
- Follow-ups: mark updates to a story from an earlier issue

## AI

| Feature | Idea |
| --- | --- |
| Topic clusters | The same story from several sources becomes one article with its angles |
| Topic research | The AI searches (SearXNG, Brave, Tavily, Kagi or the provider's own web search) for articles on topics you describe, scores them 0–100 with a reason and adds the best to the issue. Settings per topic: description, keywords, language, period, minimum score, maximum articles, allowed and blocked domains, frequency, cost limit |
| Categorize | Articles without a section go to politics, tech, sports … |
| Background box | Short explanation of terms and history |
| Weekly review | A Sunday issue with the week's big stories |
| Kids' edition | A profile where the AI explains selected news for children; age, topics and exclusions adjustable |
| Model per task | A small local model for bulk work, a stronger cloud model for the overview |
| Prompt updates | When a new default prompt ships and you changed yours, show the difference: take, merge or keep |
| Native Anthropic provider | Only if the OpenAI-compatible endpoint stops being enough (e.g. for prompt caching) |
| Result cache | Reuse summaries and scores across issues |
| Chat with the archive | "What did it say about X last week?" (experimental) |

Private source types (newsletters, calendar, Home Assistant, sources with a login) should default to "local AI only".

## Issue and layout

- PDF in newspaper layout: two columns, for reMarkable, tablets or printing
- Audio issue as a private podcast feed (experimental)
- Layout templates (classic, compact, magazine) in a sandboxed template engine; typeface, image size, QR code to the original, section colors for color e-ink
- Reading-time budget: fill the issue by priority up to e.g. 20 minutes; important texts in full, others as summary only
- Links as numbered endnotes, calmer to read on e-ink
- Closing page with odds and ends, a quote or a puzzle (sudoku, crossword)
- Weekend longread issue with the week's longer articles

## Delivery and schedule

- Storage: WebDAV, Nextcloud or a folder on the server
- Messenger delivery: Telegram or Matrix bot sends the file
- Cron expressions next to time and weekdays
- "Only send with at least x articles", otherwise skip the issue
- Trigger an issue by API or a Home Assistant button
- Detect Kindle rejection mails over IMAP and show them on the dashboard
- Shrink images harder when an issue would exceed the mail limit

## Feedback

- "Read later" and "Save to Wallabag" links under each article
- Upload `My Clippings.txt` from the Kindle: highlights go to the archive and into the interests
- Feedback suggests changes to interest weights instead of changing them silently

## Web UI

- Archive as a stack of cover thumbnails
- Sign-in with OIDC (Authentik, Authelia, Keycloak) or header auth behind a reverse proxy
- Change a user's role
- Template gallery: ready-made bundles such as "Tech morning" to import
- More UI languages (resource files)

## Operations

- PostgreSQL as an alternative to SQLite
- Backup and restore from the web UI, also on a schedule
- Export and import of the whole configuration as YAML, to keep it in Git; secrets from environment variables or Docker secrets
- Storage use per area on the dashboard with a warning threshold; `VACUUM` after cleanup
- Prometheus metrics
- REST API and webhooks, Home Assistant integration
- Sync read state back to FreshRSS or Miniflux
- `edge` image channel for pre-releases
- Limits for parallel fetches and image processing, so it runs on a Raspberry Pi 4 with 2 GB
