# 2. Interactive web UI, no Kindle browser support

- Status: accepted
- Date: 2026-11-22

## Context

The first version of the web UI was built fully static, without any JavaScript, because the concept said some pages had to work in the Kindle browser. That led to workarounds: a delete confirmation needs an extra round trip, searching reloads the page, and sections are sorted with "up" and "down" buttons instead of drag and drop.

The only things the Kindle actually touches are the feedback links in the EPUB ("more like this", "less like this"). They need an endpoint that answers a GET, not a page of the UI. Nobody manages sources or reads the web view on the Kindle.

## Decision

- No page of the web UI has to work in the Kindle browser.
- Pages stay statically rendered by default. Pages that gain from it (sources, sections, settings) use Blazor's interactive server rendering per page.
- Feedback links from the EPUB go to a minimal API endpoint that returns a short confirmation. It is not part of the UI.
- Sign-in and setup stay static: they are needed before a session exists and gain nothing from interactivity.

## Consequences

- Live search, drag and drop, dialogs and in-place updates become possible without hand-written JavaScript.
- `blazor.web.js` is loaded and needs a WebSocket connection; the Content-Security-Policy must allow it.
- Interactive pages hold state on the server per open tab. Fine for a self-hosted app with a handful of users.
- HTTP-level tests no longer cover clicks on interactive pages; those need component tests (bUnit) or browser tests.
