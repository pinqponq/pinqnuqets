# PinqNugets — Pinqponq shared infrastructure packages

[![CI](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml/badge.svg)](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Moves **generic infrastructure** pieces that were duplicated across 12 backends
(cache, SMS, mail, DB connectivity, messaging, authentication, error handling)
into `Pinqponq.*` NuGet packages in one monorepo. Goals: end **version drift**,
stop shipping the same bugs repeatedly, and replace naming/contract scatter
(e.g. `ISmsService` vs `IGSMService`) with one standard surface.

Packages hold **fixed behavior that wraps an external dependency**;
project-specific business logic (domain repositories, message contracts, OTP-role
rules) is **not** packaged.

- Targets: `net8.0`, `net9.0`, `net10.0` — .NET 8 and later (`PinqponqTargetFrameworks` in `Directory.Build.props`)
- Dependency versions via **Central Package Management** (`Directory.Packages.props`)
- License: [MIT](LICENSE)

Each package ships its own README on nuget.org (see links below). This repo README
is the monorepo overview — the fastest way to try everything is the **Playground**.

## Playground — try packages and their logs in the browser

[`samples/Pinqponq.Playground`](samples/Pinqponq.Playground) is a browser test console
that runs all 13 packages against **real** dependencies. Every run shows:

1. What the package did (steps, outputs, assertions)
2. The **structured log records** it produced (`traceId` / `correlationId` field names,
   message templates, levels) — the same shapes Pinqloq expects

```bash
dotnet run --project samples/Pinqponq.Playground
# → http://127.0.0.1:5199
```

- Packages are referenced as **source** (`ProjectReference`) — edits under `src/` show up immediately
- **No containers on launch**; most scenarios work without Docker (Identity, OTP/SMS, TOTP, SSO negatives, ErrorHandling, SMS via a fake NetGSM endpoint)
- With Docker: start Redis, Postgres, RabbitMQ, Mongo, MailHog, or SQL Server from the top bar (Testcontainers) to unlock the matching scenarios
- Full guide: [samples/README.md](samples/README.md)

## Packages

| Package | Role | Docs |
|---|---|---|
| **Pinqponq.Identity** | JWT issue/validate (HMAC+RSA), refresh token issue/rotate/revoke, PBKDF2 password hash/verify | [README](src/Pinqponq.Identity/README.md) |
| **Pinqponq.Identity.Otp** | OTP generate/send/verify; mail/sms channel routing; storage interface (`IOtpStore`) | [README](src/Pinqponq.Identity.Otp/README.md) |
| **Pinqponq.Auth.Totp** | RFC 6238 TOTP 2FA; `otpauth://` provisioning URI | [README](src/Pinqponq.Auth.Totp/README.md) |
| **Pinqponq.Auth.Sso.Abstractions** | `IExternalAuthProvider` contract | [README](src/Pinqponq.Auth.Sso.Abstractions/README.md) |
| **Pinqponq.Auth.Sso.Google** | Google id_token validation (`Google.Apis.Auth`) | [README](src/Pinqponq.Auth.Sso.Google/README.md) |
| **Pinqponq.Cache** | Redis: get/set/remove/exists, distributed lock, health-check | [README](src/Pinqponq.Cache/README.md) |
| **Pinqponq.Sms** | NetGSM SMS sending (`ISmsSender`) + retry | [README](src/Pinqponq.Sms/README.md) |
| **Pinqponq.Mail** | SMTP mail sending (`IEmailSender`, System.Net.Mail) | [README](src/Pinqponq.Mail/README.md) |
| **Pinqponq.Database.Postgres** | Npgsql connection, retry, health-check (no repository/entity) | [README](src/Pinqponq.Database.Postgres/README.md) |
| **Pinqponq.Database.Mongo** | MongoDB client, health-check (no repository/entity) | [README](src/Pinqponq.Database.Mongo/README.md) |
| **Pinqponq.Database.Mssql** | SqlClient connection, retry, health-check (no repository/entity) | [README](src/Pinqponq.Database.Mssql/README.md) |
| **Pinqponq.Messaging.RabbitMq** | Publish (confirms + mandatory) / consume, reconnect, DLX or MaxRedelivery | [README](src/Pinqponq.Messaging.RabbitMq/README.md) |
| **Pinqponq.ErrorHandling** | Global exception middleware + standard error contract + Pinqloq-compatible structured logging | [README](src/Pinqponq.ErrorHandling/README.md) |

Each package exposes an `AddPinqponqXxx(...)` DI extension (where applicable) and an options class. Install from nuget.org once published, or reference the project under `src/`.

## Build & test

Requirements: .NET 10 SDK — enough to build every target. To **run** tests on each
target you also need the .NET 8 and .NET 9 runtimes. Integration tests need a working
Docker environment (Testcontainers: Redis, RabbitMQ, PostgreSQL, MongoDB, SQL Server,
MailHog).

```bash
dotnet build -c Release   # net8.0 + net9.0 + net10.0
dotnet test -c Release --collect:"XPlat Code Coverage"
dotnet pack -c Release    # .nupkg per package (one consistent version via CPM)
```

> Dependency versions live in `Directory.Packages.props`.
> Integration tests are marked with `[Trait("Category", "Integration")]`.

## Contributing & security

- Contributing guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Security vulnerability reporting: [SECURITY.md](SECURITY.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
