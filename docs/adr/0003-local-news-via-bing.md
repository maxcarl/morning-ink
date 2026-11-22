# 3. Local news via Bing News RSS

- Status: accepted
- Date: 2026-11-22

## Context

Local news come from a news search for a town and its district (`INewsSearch`). The first version used Google News RSS. Its items carry only a headline and a link to Google, which reveals the article only with JavaScript or an undocumented request. The issue therefore got nothing but headlines, which made local news of little use.

Bing News RSS links through a click counter whose `url` parameter is the article itself, and every item has a teaser and the publisher's name.

## Decision

- Local news use Bing News RSS, sorted by date, in the language and country edition of the source's place.
- The article URL is taken from the link's `url` parameter, so the full text is fetched from the publisher like for any feed. Where that fails (paywall, no article found), the teaser stays.
- Google News is removed rather than kept as an option: it cannot deliver more than headlines.

## Consequences

- Local news read like other articles; a paywall leaves the teaser.
- The searched towns go to Microsoft instead of Google.
- Bing's RSS output is not an official API (the Bing News Search API was retired in 2025). If it changes, local sources report a failure and the issue appears without them; the news search is one class behind `INewsSearch`.
- A self-hosted SearXNG can replace it behind the same interface: its news results also carry direct article URLs and teasers, and the towns then go to no single provider. Done: `NewsSearch__SearxngUrl` switches local news to it.
