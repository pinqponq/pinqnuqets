# PinqNugets — Pinqponq shared infrastructure packages

[![CI](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml/badge.svg)](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Moves **generic infrastructure** pieces that were duplicated across 12 backends
(cache, SMS, mail, DB connectivity, messaging, authentication, error handling, observability)
into `Pinqponq.*` NuGet packages in one monorepo. Goals: end **version drift**,
stop shipping the same bugs repeatedly, and replace naming/contract scatter
(e.g. `ISmsService` vs `IGSMService`) with one standard surface.

Packages hold **fixed behavior that wraps an external dependency**;
product-specific business logic (domain repositories, message contracts, OTP-role
rules) is **not** packaged.

- Targets: `net8.0`, `net9.0`, `net10.0` — .NET 8 and later (`PinqponqTargetFrameworks` in `Directory.Build.props`)
- Dependency versions via **Central Package Management** (`Directory.Packages.props`)
- License: [MIT](LICENSE)

Each package ships its own README on nuget.org (see links below). This repo README
is the monorepo overview — the fastest way to try everything is the **Playground**.

## Playground — try packages and their logs in the browser

[`samples/Pinqponq.Playground`](samples/Pinqponq.Playground) is a browser test console
that runs all 15 packages against **real** dependencies. Every run shows:

1. What the package did (steps, outputs, assertions)
2. The **structured log records** it produced (`traceId` / `correlationId` field names,
   message templates, levels) — the same shapes Pinqloq expects

```bash
dotnet run --project samples/Pinqponq.Playground
# → http://127.0.0.1:5199
```

- Packages are referenced as **source** (`ProjectReference`) — edits under `src/` show up immediately
- **No containers on launch**; most scenarios work without Docker (Identity, OTP/SMS, TOTP, SSO negatives, ErrorHandling, LiveKit tokens/webhooks, SMS via a fake NetGSM endpoint)
- With Docker: start Redis, Postgres, RabbitMQ, Mongo, MailHog, or SQL Server from the top bar (Testcontainers) to unlock the matching scenarios
- Full guide: [samples/README.md](samples/README.md)

## Packages

| Package | Role | Docs |
|---|---|---|
| **Pinqponq.Identity** | JWT issue/validate (HMAC+RSA), refresh token issue/rotate/revoke, PBKDF2 password hash/verify | [README](src/Pinqponq.Identity/README.md) |
| **Pinqponq.Identity.Otp** | OTP generate/send/verify; mail/sms channel routing; storage interface (`IOtpStore`) | [README](src/Pinqponq.Identity.Otp/README.md) |
| **Pinqponq.Auth.Totp** | RFC 6238 TOTP 2FA; `otpauth://` provisioning URI | [README](src/Pinqponq.Auth.Totp/README.md) |
| **Pinqponq.Auth.ApiKey** | `[RequireApiKey]` action filter: shared-secret header check for service-to-service endpoints, named keys, constant-time comparison | [README](src/Pinqponq.Auth.ApiKey/README.md) |
| **Pinqponq.Auth.Sso.Abstractions** | `IExternalAuthProvider` contract | [README](src/Pinqponq.Auth.Sso.Abstractions/README.md) |
| **Pinqponq.Auth.Sso.Google** | Google id_token validation (`Google.Apis.Auth`) | [README](src/Pinqponq.Auth.Sso.Google/README.md) |
| **Pinqponq.Cache** | Redis: get/set/remove/exists, distributed lock, health-check | [README](src/Pinqponq.Cache/README.md) |
| **Pinqponq.Sms** | NetGSM SMS sending (`ISmsSender`) + retry | [README](src/Pinqponq.Sms/README.md) |
| **Pinqponq.Mail** | SMTP mail sending (`IEmailSender`, System.Net.Mail) | [README](src/Pinqponq.Mail/README.md) |
| **Pinqponq.Database.Postgres** | Npgsql connection, retry, health-check (no repository/entity) | [README](src/Pinqponq.Database.Postgres/README.md) |
| **Pinqponq.Database.Mongo** | MongoDB client, health-check (no repository/entity) | [README](src/Pinqponq.Database.Mongo/README.md) |
| **Pinqponq.Database.Mssql** | SqlClient connection, retry, health-check (no repository/entity) | [README](src/Pinqponq.Database.Mssql/README.md) |
| **Pinqponq.Messaging.RabbitMq** | Publish (confirms + mandatory) / consume, reconnect, DLX or MaxRedelivery | [README](src/Pinqponq.Messaging.RabbitMq/README.md) |
| **Pinqponq.Database.Postgres.EntityFrameworkCore** | EF Core `DbContext` on PostgreSQL with one shared `NpgsqlDataSource` per context | [README](src/Pinqponq.Database.Postgres.EntityFrameworkCore/README.md) |
| **Pinqponq.ErrorHandling** | Global exception middleware + standard error contract + localized messages + Pinqloq-compatible structured logging | [README](src/Pinqponq.ErrorHandling/README.md) |
| **Pinqponq.ErrorHandling.Postgres** | PostgreSQL error mapping (`database_*` codes, SQLSTATE log detail) for Pinqponq.ErrorHandling | [README](src/Pinqponq.ErrorHandling.Postgres/README.md) |
| **Pinqponq.RateLimiting** | Rate limiting in one call: fixed-window policies by client address, user or a caller-supplied key (hashed when secret), chained global limits, 429 with `Retry-After` and the standard error body | [README](src/Pinqponq.RateLimiting/README.md) |
| **Pinqponq.Observability** | Pinqloq request logging, `ILogService` for app/job logs | [README](src/Pinqponq.Observability/README.md) |
| **Pinqponq.Observability.Abstractions** | `ILogService` without dependencies, for domain/application layers | [README](src/Pinqponq.Observability.Abstractions/README.md) |
| **Pinqponq.Contracts** | Response envelope (deveng-networking-kmp compatible), paging models, common error codes, request error feature | [README](src/Pinqponq.Contracts/README.md) |
| **Pinqponq.LiveKit.Server** | LiveKit access token issuing, RoomService over Twirp JSON (rooms, participants), webhook signature verification | [README](src/Pinqponq.LiveKit.Server/README.md) |
| **Pinqponq.Configuration.Vault** | HashiCorp Vault configuration provider: one KV v2 record shaped like `appsettings.json`, Vault CLI token lookup | [README](src/Pinqponq.Configuration.Vault/README.md) |
| **Pinqponq.Billing.RevenueCat** | RevenueCat webhook handling: payload model, Authorization header check, event dispatch; storage via `IRevenueCatSubscriptionStore` | [README](src/Pinqponq.Billing.RevenueCat/README.md) |
| **Pinqponq.Push.Fcm** | Firebase Cloud Messaging sender: token, data-only and topic pushes, topic subscriptions, per-token results with invalid-token and provider-credential flags; invalid tokens via `IFcmInvalidTokenHandler` | [README](src/Pinqponq.Push.Fcm/README.md) |
| **Pinqponq.Push.DeviceTokens** | Device token store on EF Core: register, move a token to the latest user, replace a rotated token on a known device, remove dead tokens (also those `IFcmInvalidTokenHandler` reports); optional endpoints and an after-registration hook | [README](src/Pinqponq.Push.DeviceTokens/README.md) |
| **Pinqponq.Billing.RevenueCat.Mail** | Subscription event mails on top of Billing.RevenueCat and Mail: enabled events, recipient lookup via `IRevenueCatMailRecipientResolver`, branded HTML + plain text, per-language copy | [README](src/Pinqponq.Billing.RevenueCat.Mail/README.md) |
| **Pinqponq.Storage.S3** | S3 and S3-compatible object storage (Cloudflare R2, MinIO): upload, download, presigned URLs, metadata, copy, delete, bucket health check | [README](src/Pinqponq.Storage.S3/README.md) |
| **Pinqponq.Hosting.Periodic** | Periodic background jobs: on/off switch, startup delay, a new DI scope per run, failures logged without stopping the host, observer hook, optional gate against overlapping runs across replicas, health check for the last successful run | [README](src/Pinqponq.Hosting.Periodic/README.md) |
| **Pinqponq.Hosting.Cors** | CORS policy from a configured origin allowlist: no default origins, optional credentials, Development-only switch for an empty list, startup validation of the origins | [README](src/Pinqponq.Hosting.Cors/README.md) |

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

## Releasing packages

Packages are published to nuget.org by CI. Nobody uploads them by hand.

**To release a package (for a person or an AI agent):**
1. In the package's `.csproj`, raise `<Version>` (Semantic Versioning). Each package has its own version.
2. Add a line under `[Unreleased]` in `CHANGELOG.md`.
3. Open a pull request. Merge it into `master` when CI is green.
4. After the merge, the `publish` job in `.github/workflows/ci.yml` runs. It pushes every package whose version is not yet on nuget.org. A package whose `<Version>` you did not raise is skipped.
5. Check the run under the repository's **Actions** tab. The `NuGet login` and `Push to nuget.org` steps must be green. The new version shows up on nuget.org a few minutes later, after nuget.org has validated it.

**How the login works.** CI does not store a nuget.org API key. It uses NuGet Trusted Publishing: GitHub gives the job a short-lived OIDC token, nuget.org checks it against a policy and returns an API key that is valid for one hour. The pieces:
- nuget.org policy (account `pinqponq` > Trusted Publishing): repository owner `pinqponq`, repository `pinqnuqets`, workflow file `ci.yml`, no environment, scope "Push new packages and package versions", glob `Pinqponq.*`.
- Repository secret `NUGET_USER`: the nuget.org profile name (not an e-mail address).
- The `publish` job needs `permissions: id-token: write`. If you rename `ci.yml` or move the `publish` job to another workflow file, update the nuget.org policy as well, or the login fails.

**If publishing fails**
- `NuGet login` fails: the policy does not match (owner, repository or workflow file name), or the policy owner cannot publish the `Pinqponq.*` packages.
- `Push to nuget.org` is rejected with 403: the policy scope or glob pattern does not cover the package. A package with a new ID needs the scope "Push new packages and package versions".
- The run is green but the version is missing on nuget.org: nuget.org is still validating it, wait a few minutes.
- Re-run the `publish` job from the Actions tab after you fix the cause. Versions that are already published are skipped, so a re-run is safe.

**Manual fallback.** Only if CI cannot be used: `dotnet build Pinqponq.Nugets.sln -c Release`, then `dotnet pack Pinqponq.Nugets.sln -c Release --no-build -o ./artifacts/nupkg`, then upload the `.nupkg` files on nuget.org under **Upload**. Never commit an API key to the repository.

## Contributing & security

- Contributing guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Security vulnerability reporting: [SECURITY.md](SECURITY.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
