# Security policy

Morning Ink fetches untrusted content from the internet and stores credentials, so security reports are very welcome.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through [GitHub's vulnerability reporting](https://github.com/maxcarl/morning-ink/security/advisories/new) instead.

Include what you found, how to reproduce it and which version or image tag you used. You will get an answer within a week. Once a fix is released, the advisory is published with credit to you, unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes. Update with `docker compose pull && docker compose up -d`.

## Scope

Of particular interest:

- Requests into the home network despite the block (SSRF), e.g. through redirects or DNS
- Script or markup from feeds, pages, mails or AI answers reaching the web UI or the EPUB
- Credentials leaking to other hosts, into logs or into error messages
- Access to pages, issues or files without signing in, or to another user's data
- Forged feedback links

Out of scope: attacks that need admin access or full access to the `/data` volume, and missing hardening of the reverse proxy in front of Morning Ink.
