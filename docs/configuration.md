# Configuration

All settings with their defaults. Settings come from `appsettings.json` or environment variables (`Section__Key`, array entries as `Section__Key__0`). Mail (SMTP) settings and home network exceptions will also be editable in the web UI; values set by environment variables take precedence and appear there read-only.

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
| `Scheduling__CatchUpWindow` | `12:00:00` | An issue missed while Morning Ink was off is sent at start if its time is at most this long ago. |
| `Scheduling__FirstRetryDelay` | `00:05:00` | Wait before retrying a failed delivery; each further retry waits three times as long. |
| `Scheduling__MaxDeliveryAttempts` | `5` | Delivery attempts per issue before giving up. |
| `Retention__IssueDays` | `30` | Issues and their files are deleted after this many days, unless pinned. |
| `Retention__SeenArticleDays` | `90` | Articles count as seen this long. Keep it longer than any source's maximum article age. |
| `Retention__ImageDays` | `7` | Converted images are deleted after this many days; delivered issues keep theirs inside the EPUB. |
| `Retention__KeepBackups` | `5` | Database backups kept. |
| `Retention__CleanupTime` | `03:30` | Daily cleanup time, in UTC. |
| `Weather__GeocodingUrl` | Nominatim (OpenStreetMap) | Turns postal codes and towns into places; asked only when a region changes. Can be a self-hosted Nominatim. |
| `Weather__CountryCodes` | – | Countries to look up regions in when a profile or local source has no country (set in the UI); empty searches worldwide. |
| `Weather__ForecastUrl` | Open-Meteo | Weather forecast; coordinates are sent rounded to about a kilometre. Can be a self-hosted Open-Meteo. |
| `NewsSearch__BingNewsUrl` | Bing News RSS | News search for local news sources. The towns searched for go to Microsoft; articles are then fetched from the publishers. |
| `NewsSearch__SearxngUrl` | – | Your own SearXNG, e.g. `https://searx.example.com`; also under Settings → Local news, where this variable shows read-only. Set, it replaces Bing for local news and the towns stay with your instance. It must allow JSON (`search.formats: [html, json]` in its `settings.yml`); saving it on the settings page allows its host in the home network; set by variable, allow it with `Http__AllowedHosts__0`. |
| `NewsSearch__Language`, `NewsSearch__Country` | `de`, `DE` | News edition when a local source has no country; with a country, its own edition is used (e.g. `en`/`US`). |
| `Notifications__TelegramApiUrl` | `https://api.telegram.org` | Telegram Bot API for edition notifications; can be a self-hosted Bot API server. ntfy, Gotify and Telegram tokens are set per edition in the UI and stored encrypted. |
| `Images__MinDimension` | `100` | Images narrower or lower than this (icons, avatars, tracking pixels) are dropped. |
| `Images__MaxDimension` | `1000` | Longest side of converted images, in pixels. |
| `Images__MaxPixels` | `40000000` | Larger images are rejected before decoding. |
| `Images__MaxDownloadBytes` | `5242880` | Largest image downloaded (5 MiB). |
| `Images__MaxEncodedBytes` | `204800` | Largest converted JPEG (200 KiB); quality and size go down until it fits. |
| `Images__MaxPerArticle` | `10` | Images kept per article. |
| `Images__Quality` | `75` | JPEG quality of the first attempt. |

## Behind a reverse proxy

A reverse proxy such as Nginx Proxy Manager, Caddy or Traefik takes the HTTPS connection and passes it on to Morning Ink as plain HTTP. Without further settings, Morning Ink sees every request as "HTTP from the proxy", so:

- the OPDS address on the account page starts with `http://` instead of `https://`,
- the sign-in cookie is not marked as HTTPS-only,
- the limit on sign-in attempts counts all clients as one: someone guessing passwords from outside locks you out too, for a minute.

Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` to fix all three: Morning Ink then takes the browser's scheme and address from the headers the proxy adds (`X-Forwarded-Proto`, `X-Forwarded-For`).

It trusts these headers from anyone who reaches its port. So let only the proxy reach it: put both containers in one Docker network and do not publish port 8080. For Nginx Proxy Manager:

```yaml
services:
  morning-ink:
    image: ghcr.io/maxcarl/morning-ink:latest
    environment:
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"
    networks: [proxy]          # the network Nginx Proxy Manager is in
    # no "ports:": only the proxy reaches Morning Ink
    volumes:
      - morning-ink-data:/data

networks:
  proxy:
    external: true
```

In Nginx Proxy Manager, forward to host `morning-ink`, port `8080`, and enable "Websockets Support" (the pages need it).

If the proxy forwards to the host's published port 8080 instead, it works as well; then anyone in your home network who reaches port 8080 directly could pretend to come from another address. At home that is a small risk; the setup above avoids it.
