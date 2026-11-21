# 1. Encrypted credentials with ASP.NET Data Protection

- Status: accepted
- Date: 2026-11-21

## Context

Sources and SMTP need passwords and tokens. They must not be stored in plain text, must survive restarts and must never show up in logs, exports or error messages. Morning Ink runs as a single container without an external secret store.

## Decision

- Secrets are encrypted with ASP.NET Core Data Protection through an EF Core value converter (`ProtectedStringConverter`). Domain code only ever sees plain strings and knows nothing about encryption.
- The key ring lives in `/data/keys`, next to but outside the database. The application name is fixed (`MorningInk`), so keys stay valid when the container or path changes.
- The EF model cache is keyed by the Data Protection provider (`DataProtectionModelCacheKeyFactory`), because the converter captures a protector.

## Consequences

- A leaked database file or backup alone does not reveal secrets; the key ring is needed as well.
- Anyone with access to the whole `/data` volume can decrypt the secrets. On Linux the keys are not encrypted at rest; encrypting them with a certificate is possible later.
- Losing `/data/keys` makes all stored secrets unreadable. Backups must contain the key ring; restoring a database without it means re-entering credentials.
