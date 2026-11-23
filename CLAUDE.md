# CLAUDE.md – Morning Ink

Self-hosted service that builds a personal newspaper (EPUB) from feeds and other sources every morning and delivers it to Kindle and other e-readers. One Docker container, Blazor web UI, optional AI. Features: [README](README.md), ideas: [docs/roadmap.md](docs/roadmap.md), settings: [docs/configuration.md](docs/configuration.md).

## Commands

```bash
dotnet build                          # must build without warnings
dotnet test                           # all tests
dotnet format --verify-no-changes     # formatting, also in CI
dotnet run --project src/MorningInk.Web
docker compose up --build
```

## Architecture

- Clean Architecture: `Domain` → `Application` → `Infrastructure` / `Web`. Dependencies point inward only; `Domain` and `Application` reference no EF Core, ASP.NET or HTTP libraries. Architecture tests enforce this; never weaken them.
- Extensions go through interfaces (`ISource`, `IProcessor`, `IRenderer`, `IDelivery`): a new source is a new class plus registration, without reworking existing code.
- Multi-user from the ground up: profiles, sources and credentials always belong to a user.
- `Web` is the composition root; registrations live in per-layer extension methods, `Program.cs` stays short.

## Decisions

- **Secrets** are encrypted with ASP.NET Data Protection via an EF value converter; the key ring is in `/data/keys`, application name fixed to `MorningInk`. Losing the keys means re-entering secrets.
- **Web UI** renders statically by default; pages that gain from it use interactive server rendering. No page has to work in the Kindle browser. Feedback links from the EPUB hit a minimal signed GET endpoint, not a UI page. Sign-in and setup stay static.
- **Local news** come from Bing News RSS (article URL from the link's `url` parameter, so full text works) or a self-hosted SearXNG, behind `INewsSearch`. The searched town goes to that provider.
- **AI** is one provider type: an OpenAI-compatible chat endpoint through Microsoft.Extensions.AI (covers Ollama, LM Studio, OpenAI, Anthropic, OpenRouter). Settings live in the database, the key encrypted. Marking the provider local allows its host in the home network. AI never costs an article: any failure leaves the article as it is, and the rest of the run stops asking.

## Code

- Code, identifiers, comments, commits and docs in English. UI texts in resource files, English and German.
- Nullable on, warnings are errors. No `!` without a comment on why it is safe.
- Small single-purpose classes, no magic strings or numbers, DI everywhere, options pattern for settings.
- `TimeProvider`, never `DateTime.Now`/`UtcNow`. HTTP only via `IHttpClientFactory` and the safe client.
- Expected failures (source offline, login expired, invalid feed) are result types, not exceptions.
- Structured logging with placeholders. `async` throughout, pass `CancellationToken` on.
- XML doc comments on public types and members, plus comments that explain why.

## Security (non-negotiable)

Everything from outside is untrusted: URLs, feeds, pages, mails, AI answers, user input.

- **Fetching:** `http`/`https` only. Internal addresses (loopback, private ranges, link-local, IPv6 equivalents) blocked after DNS resolution and after every redirect; exceptions only via the explicit allowlist. Max 5 redirects, size limit, timeouts.
- **Credentials:** sent only to their own domain; headers and cookies dropped on a cross-domain redirect. Stored encrypted, never in logs, exports or error messages.
- **XML:** no external entities, no DTD processing.
- **HTML:** strict allowlist sanitizing before EPUB and web view. No scripts, event handlers or `style` with URLs.
- **Images:** check pixel and byte limits before decoding, always re-encode.
- **Files:** names from hash or ID, never from foreign content; all paths stay under the data directory.
- **Database:** EF Core or parameterized queries only.
- **Mail:** no line breaks in subjects and file names.
- **Web:** every page and file needs a login; CSRF protection; CSP; article preview in a sandboxed iframe.
- **External programs:** arguments as a list, never through a shell.
- **EPUB feedback links:** HMAC-signed with expiry.
- **AI:** article text goes to the model fenced as data, never as instructions. No tools with side effects; answers are validated and can never change settings. "Local AI only" sources never go to a cloud provider. Without AI the issue still appears. Default prompts are versioned in the repo; user prompts are never overwritten.

## Tests

- Write the tests for the task's acceptance criterion first.
- Unit tests for `Domain` and `Application`, integration tests for `Infrastructure`, in-memory app tests and bUnit for `Web`.
- No real network calls; saved feeds and pages live under `TestData/`, with neutral names and places.
- Every EPUB the tests build is checked with epubcheck in CI.
- Security rules get their own tests (redirects into the home network, `file:` URLs, …).

## Workflow

- One task per session; don't touch unrelated code.
- Ask before adding a NuGet package; check its license and add it to `THIRD-PARTY-NOTICES.md`.
- Small commits, Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`, `chore:`).
- When something is unclear or contradicts the roadmap: ask, don't guess.
- Done means: acceptance criterion met and tested, build without warnings, tests green, format clean, no secrets or personal data, new settings documented in `docs/configuration.md`, new UI texts in both resource files.
