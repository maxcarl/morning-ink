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
| `Images__MinDimension` | `100` | Images narrower or lower than this (icons, avatars, tracking pixels) are dropped. |
| `Images__MaxDimension` | `1000` | Longest side of converted images, in pixels. |
| `Images__MaxPixels` | `40000000` | Larger images are rejected before decoding. |
| `Images__MaxDownloadBytes` | `5242880` | Largest image downloaded (5 MiB). |
| `Images__MaxEncodedBytes` | `204800` | Largest converted JPEG (200 KiB); quality and size go down until it fits. |
| `Images__MaxPerArticle` | `10` | Images kept per article. |
| `Images__Quality` | `75` | JPEG quality of the first attempt. |
