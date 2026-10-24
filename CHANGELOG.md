# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Pinqponq.OpenApi` 1.0.0: OpenAPI and the Scalar reference in one call. `AddPinqponqOpenApi(configure)` or
  `AddPinqponqOpenApi(section, configure)` registers the document `v1`, and `MapPinqponqOpenApi()` serves it at
  `/openapi/{documentName}.json` with the Scalar reference at `/scalar/v1` (both routes are options). The document is
  served only in Development unless `ExposeIn` says otherwise (`Always`, `Never`), and `AuthorizationPolicy` can require
  a policy or a signed-in user. The Bearer scheme is on by default and is published only when the `Bearer`
  authentication scheme is registered. An API key scheme is off by default and needs a `HeaderName`. A security
  requirement is added only to operations with `[Authorize]` or `RequireAuthorization()` and no `[AllowAnonymous]`
  (`SecurityRequirementScope.AllOperations` changes that), and each published scheme is its own alternative requirement.
  `ServerUrlFromRequest` (default on) sets `servers` to the request's scheme, host and path base. `SchemaIdNaming.FullName`
  builds schema ids from the full type name. `IPinqponqOpenApiCustomizer` changes the document or an operation after the
  package has built it. Options are validated on startup. Targets `net10.0` only, because it is written against
  Microsoft.OpenApi 2.x. Pins Microsoft.OpenApi 2.7.5, because the 2.0.0 that Microsoft.AspNetCore.OpenApi 10.0.10
  asks for has a known vulnerability.
- `Pinqponq.RateLimiting` 1.0.0: ASP.NET Core rate limiting in one call. `AddPinqponqRateLimiting(configure)` or
  `AddPinqponqRateLimiting(section, configure)` registers named fixed-window `Policies` (for `[EnableRateLimiting]` and
  `RequireRateLimiting`) and `GlobalLimits` (chained when there are several, so a request must pass each one).
  Requests are counted by `ClientAddress`, `User`, `UserOrClientAddress` or `Custom`. `User` reads the
  `UserIdClaimType` claim (default `ClaimTypes.NameIdentifier`) and does not limit requests without a user. `Custom`
  uses the value of `PartitionKeyResolver`, and the client address when it returns nothing. `IsPartitionKeySecret`
  keeps only the SHA-256 digest of that value. Requests without a client address share one partition, and a warning
  is logged once. A rejected request gets 429, `Retry-After` in whole seconds rounded up (at least 1) and the
  standard error body with `too_many_requests`. An `IRateLimitRejectionWriter` replaces the body.
  `PermitLimit` and `Window` have no defaults and are validated at startup. Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.ErrorHandling` 1.2.0: `IErrorResponseWriter`, registered by `AddPinqponqErrorHandling`, writes the
  standard error body for a failure answered without an exception (localized when error localization is on) and
  describes it on `IRequestErrorFeature`. The catalogs ship `too_many_requests` in every language.
- `Pinqponq.Contracts` 1.1.0: `CommonErrorCodes.TooManyRequests` (`too_many_requests`).
- `Pinqponq.Hosting.Periodic` 1.0.0: periodic background jobs. `AddPinqponqPeriodicJob<TJob>(configure)` and
  `AddPinqponqPeriodicJob<TJob>(section)` register an `IPeriodicJob` as a scoped service and one hosted service
  that runs it. The options are `IsEnabled` (default `true`), `StartupDelay` (default zero), `RunImmediately`
  (default `true`) and `Interval` (required, no default). Every run gets a new DI scope that is disposed after
  the run. A failed run is logged with the job name, and the next run still starts. Stopping the host ends the
  job without an error log. Options are validated on startup. `IPeriodicJobObserver` receives started,
  succeeded (with duration) and failed (with exception) calls. `IPeriodicJobGate` can skip a run, for example
  while another replica runs the job. `AddHealthChecks().AddPinqponqPeriodicJob<TJob>()` reports a job that has
  not finished a run successfully for two intervals (or `staleAfter`), and a job that was never registered.
  Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.Push.Fcm` 1.1.0: `FcmOptions.IsSendFailureLoggingEnabled` (default `true`) lets an application that logs the
  returned `FcmSendResult` list itself turn off the package's own send-failure log records. 1.0.0 stays valid.
- `Pinqponq.Push.Fcm` 1.0.0: the Firebase Cloud Messaging sender extracted from a product backend.
  `AddPinqponqFcm(section)` / `AddPinqponqFcm(configure)` register `IFcmSender`: `SendToTokens`,
  `SendDataOnlyToTokens`, `SendToTopic`, `SubscribeToTopic`, `UnsubscribeFromTopic`. Sends to tokens
  return an `FcmSendResult` per token (`IsTokenInvalid` for `Unregistered`, `IsProviderCredentialInvalid`
  for `ThirdPartyAuthError`) and pass invalid tokens to an optional `IFcmInvalidTokenHandler`. Token lists
  are split into FCM's 500 (send) and 1,000 (topic) limits. `FcmOptions` (`IsEnabled`, `CredentialPath`
  or `CredentialJson`, `DataOnlyAndroidPriority`, default `Normal`) are validated at startup.
  `IsSendFailureLoggingEnabled` (default `true`) can turn off the send failure log records. The package
  uses its own named Firebase app and logs failures through `ILogService` with token suffixes only.
  FirebaseAdmin 3.4.0. Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.Billing.RevenueCat.Mail` 1.0.0: subscription event mails. `AddPinqponqRevenueCatMail<TResolver>`
  registers an `IRevenueCatEventListener` that maps `INITIAL_PURCHASE`, `RENEWAL`, `CANCELLATION`,
  `BILLING_ISSUE` and `EXPIRATION` to mails (only `InitialPurchase` enabled by default), asks the product's
  `IRevenueCatMailRecipientResolver` for the address and language, renders a branded HTML mail with a
  plain-text version through `IRevenueCatMailRenderer` (default layout, per-event `TemplateOverride`, every
  value HTML-encoded) and sends it through `IEmailSender`. A failure is logged and reported to
  `IRevenueCatWebhookObserver` (`RevenueCatMailLogEvents.Sent/Skipped/Failed`) and never fails the webhook;
  a request cancellation propagates. Options are validated on startup. Defaults, none taken from a product:
  English texts for every event that name only the brand, `DefaultLanguage` `en`, `InitialPurchase` the only
  enabled event, `RequirePremiumEntitlement` on, `PrimaryColor` `#333333`, `OnPrimaryColor` `#ffffff`, no logo, footer = brand name. Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.Mail` 1.1.0: `EmailMessage.PlainTextBody`. When set on an HTML mail, the mail is sent as
  multipart/alternative with the plain text first and the HTML last, so clients without HTML show the text.
- `Pinqponq.Storage.S3` 1.0.0: object storage on AWS S3 and S3-compatible stores (Cloudflare R2, MinIO).
  `AddPinqponqObjectStorage` (configuration section or `Action<ObjectStorageOptions>`) registers one
  `IAmazonS3` client and `IObjectStorage`: `Upload`, presigned `CreateDownloadUrl` / `CreateUploadUrl`
  (UTC expiry, the upload URL signs the content type), `GetMetadata` (null for a missing key), `Download`,
  `DownloadToFile` (`ObjectNotFoundException` for a missing key), `Copy` and `Delete`. `ObjectStorageOptions`
  is validated at startup (bucket, keys, and exactly one of `ServiceUrl` or `Region`); `UseCloudflareR2(accountId)`
  sets the R2 endpoint, path-style addressing, unsigned payloads, `WhenRequired` checksums and the `auto`
  signing region. `AddPinqponqObjectStorageHealthCheck` sends a HEAD request for the bucket. No bucket,
  region or key layout defaults. Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.Auth.ApiKey` 1.0.0: the shared-secret header check for service-to-service endpoints.
  `AddPinqponqApiKey(name, configure)` registers a named `ApiKeyOptions` (`HeaderName`, required and
  validated on startup; `ApiKey`) and `[RequireApiKey(name)]` returns 401 for a missing, empty or wrong
  header and when no key is configured. Keys are compared in constant time (SHA-256 digests with
  `CryptographicOperations.FixedTimeEquals`). No default header name or key. Targets `net8.0`, `net9.0`
  and `net10.0`.
- `Pinqponq.Billing.RevenueCat` 1.0.0: RevenueCat webhook handling extracted from a product backend.
  `AddPinqponqRevenueCat<TStore>` registers `IRevenueCatEventHandler` (idempotency by event id, activation,
  cancellation, expiration, billing issue, `TRANSFER`, and a guard that ignores `CANCELLATION`/`EXPIRATION`/
  `BILLING_ISSUE` events of a superseded subscription period) and `IRevenueCatWebhookAuthenticator`
  (constant-time Authorization header check). The product supplies storage through
  `IRevenueCatSubscriptionStore`; an optional `IRevenueCatWebhookObserver` forwards structured step records
  to its own log pipeline, and `IRevenueCatEventListener`s run follow-ups (a welcome mail, say) after an
  event is committed. Targets `net8.0`, `net9.0` and `net10.0`.
- `Pinqponq.Contracts` 1.0.0: the response envelope `BaseResponseModel` (`status`, `statusCode`,
  `message`, `responseCode` — compatible with deveng-networking-kmp's `ErrorResponse`, documented in
  the package README), `PagedRequestModel` / `PagedResponseModel<T>` with `ToPagedResponse`,
  `CommonErrorCodes`, and `IRequestErrorFeature`, the `HttpContext.Features` entry through which
  Pinqponq.ErrorHandling hands a failed request's event and detail to Pinqponq.Observability without
  either package depending on the other.
- `Pinqponq.Contracts` paging, the shared offset pagination of every Pinqponq API (pinqponq/org#218):
  `ToPagedResponse` also pages an `IQueryable<T>` (one count, one page read), `PageWindow` lets you
  page any async query by the same rules as `ToPagedResponse`, and `PaginationOptions` sets the page
  size cap (default 100) and what the page after the last page returns (`ReturnLastPage`, the
  default, or `ReturnEmptyPage`).
- `Pinqponq.Observability` 1.0.0: Pinqloq logging for ASP.NET Core — `RequestLoggingMiddleware`
  (one record per request with redacted bodies and route-based event names), `ILogService` /
  `LogService` (ILogger + Pinqloq, exception fields, 4,000-character stack trace trimming). Collection
  names are required and validated at
  startup; the device identifier header, extra redacted keys and logged `HttpContext.Items` entries
  are settings with empty defaults.
- `Pinqponq.Observability.Abstractions` 1.0.0: `ILogService` alone, without dependencies, for domain
  and application layers; Pinqponq.Observability references it and registers the implementation.
- `Pinqponq.ErrorHandling.Postgres` 1.0.0: maps Npgsql `PostgresException` (also wrapped in an EF Core
  `DbUpdateException`) to `database_*` codes — constraint and value errors 400, the rest 500
  `database_error` — with the SQLSTATE detail in the log event and the codes'
  messages in 12 languages.
- `Pinqponq.Database.Postgres.EntityFrameworkCore` 1.0.0: `AddPinqponqPostgresDbContext<TDbContext>`
  registers an EF Core context on PostgreSQL with one `NpgsqlDataSource` per context and a hook to
  configure it (e.g. dynamic JSON). Kept apart from `Pinqponq.Database.Postgres` so services
  without EF Core do not take the dependency.

- `Pinqponq.Configuration.Vault` 1.0.0: a HashiCorp Vault configuration provider. `AddPinqponqVault`
  reads one KV version 2 record shaped like `appsettings.json` at startup and lays it over the
  configuration sources registered before it, so secrets leave the settings files while every
  `IOptions<T>` binding keeps working. The token is resolved the way the Vault CLI does it —
  the `Token` setting, then `VAULT_TOKEN`, then the `~/.vault-token` file written by
  `vault login` — so a developer who logged in once needs no per-project setup. Startup fails
  fast with `VaultConfigurationException` on a missing setting, a missing record, a rejected
  token or an unreachable Vault (the last one after `MaxAttemptCount` attempts); messages never
  contain a secret value or the token. Requests are plain `HttpClient` calls, so no Vault client
  library is added. Targets `net8.0`, `net9.0` and `net10.0`.

### Changed

- `Pinqponq.ErrorHandling` 1.1.0: optional localized error responses (`AddPinqponqErrorLocalization`:
  the response code is looked up in a message catalog — embedded `general_error`/`api_*` messages
  in 12 languages plus the application's `Localization/*.json` — and the message is returned in the
  request's language, `Accept-Language` by default), pluggable `IExceptionResolver`s, and every
  handled failure — also a request the client closed (499) — is described on `IRequestErrorFeature`.
  Existing behavior is unchanged unless these are turned on.

- Packages are versioned individually from now on: each carries its own `<Version>` in its project file and the shared `Version` in `Directory.Build.props` is gone. Every package starts at the version it has on nuget.org (1.0.1, and `Pinqponq.Identity` 1.0.2). Up to and including 1.0.2 all packages shared one version.
- `Pinqponq.Database.Mongo` 1.1.0: `ToPagedResponseAsync` pages a find into Pinqponq.Contracts'
  `PagedResponseModel<T>` (pinqponq/org#218). The package now references Pinqponq.Contracts and,
  through it, the ASP.NET Core shared framework.
- `Pinqponq.Database.Mssql` 1.0.2: `Microsoft.Data.SqlClient` 5.2.2 → 7.1.1. This is a major
  step of the driver, and consumers resolve to it through the package: read the
  `Microsoft.Data.SqlClient` 6.0 and 7.0 release notes before upgrading.
- `Pinqponq.Auth.Sso.Google` 1.0.2: `Google.Apis.Auth` 1.70.0 → 1.77.0.
- `Pinqponq.LiveKit.Server` 1.0.2: `Microsoft.Bcl.Memory` 9.0.18 → 10.0.12 on the `net8.0` leg.
- Test and build tooling: `Microsoft.AspNetCore.TestHost` 8.0.11 → 8.0.31, `coverlet.collector`
  6.0.4 → 10.1.0, and the CI actions `actions/checkout` 4 → 7, `actions/setup-dotnet` 4 → 6,
  `actions/upload-artifact` 4 → 7. No shipped package changes.

### Fixed

- `Pinqponq.Database.Mssql` 1.0.3: `Microsoft.Data.SqlClient` 7.1.1 pulled
  `System.Security.Cryptography.Pkcs` 8.0.1 (`net8.0`) and 9.0.18 (`net9.0`, `net10.0`), which
  carry GHSA-2fmv-rv2c-59x7 (moderate) and GHSA-846f-f764-76mx (high); restore failed with
  `NU1902`/`NU1903`. The package now floors it at 8.0.2 on `net8.0` and 9.0.21 on `net9.0` and
  `net10.0`, so consumers resolve the patched version through it. No code changes.
- Restore failed with `NU1903` on every branch: `Testcontainers` 4.13.0 pulled `SSH.NET`
  2025.1.0, which carries two high severity advisories (GHSA-mggc-4xg6-vcxf,
  GHSA-q939-rpr3-3284). `Testcontainers` 4.15.0 ships `SSH.NET` 2026.0.0. Only the test
  projects and the Playground referenced it; no shipped package was affected.

## [1.0.2] - 2026-09-29

### Added

- `Pinqponq.LiveKit.Server`: a LiveKit server API client — `AccessTokenIssuer` (access
  tokens), `RoomServiceClient` (RoomService over Twirp JSON: list rooms and participants,
  remove a participant, delete a room) and `WebhookReceiver` (webhook signature and body
  checksum verification), registered with `AddPinqponqLiveKitServer`. Targets `net8.0`,
  `net9.0` and `net10.0`; the `net8.0` leg takes `Microsoft.Bcl.Memory` for `Base64Url`,
  which .NET 9 ships in the BCL. It is published at the shared **1.0.1**, the version the
  rest of the packages are already on, so the family stays on one number.
- `Pinqponq.Identity`: `AddPinqponqJwtBearer`, which builds ASP.NET Core's
  `TokenValidationParameters` from the `JwtOptions` `AddPinqponqIdentity` already binds, so a
  service that validates a token cannot drift away from the one that signed it. A second overload
  binds `JwtOptions` itself, so a service that only validates tokens does not have to register the
  refresh token service and the password hasher it will never call. `Pinqponq.Identity` now
  references `Microsoft.AspNetCore.Authentication.JwtBearer`, which brings the ASP.NET Core
  shared framework with it; every consumer so far is an ASP.NET Core API.
- Playground scenarios for `Pinqponq.LiveKit.Server`: token issuing and grant checks,
  webhook verification and tamper rejection, options validation, and RoomService against
  a LiveKit server you supply. The console now covers all 14 packages.

### Changed

- `Microsoft.IdentityModel.JsonWebTokens` / `Microsoft.IdentityModel.Tokens` 8.3.0 → 8.19.2.
  `Livekit.Server.Sdk.Dotnet`, which `Pinqponq.LiveKit.Server.Tests` checks wire
  compatibility against, requires 8.14.0 through `System.IdentityModel.Tokens.Jwt`, and
  `Microsoft.AspNetCore.Authentication.JwtBearer` 9/10, which `Pinqponq.Identity` now
  references, raises that floor to 8.19.2. Transitive pinning keeps every project on one
  version — so `Pinqponq.Identity` now depends on 8.19.2 as well. Its published 1.0.1 still
  carries the 8.3.0 floor; 1.0.2 is the first release with 8.19.2.

## [1.0.1] - 2026-08-07

### Added

- Source Link and symbol packages (`snupkg`) for nuget.org debugging
- Dependabot weekly updates for NuGet and GitHub Actions

## [1.0.0] - 2026-08-07

### Added

- First public release on [nuget.org](https://www.nuget.org)
- Package icon (`pinqnuqets-dark.png`) for all packable packages
- English package descriptions, README, and Playground sample UI

### Changed

- Package version **1.0.0** (first stable public SemVer release)
- Copyright notice set to `Copyright (c) Pinqponq 2026`

## [0.2.1] - 2026-07-25

### Added

- `ITotpReplayStore` + `ITotpService.ValidateAsync` (sync `Validate` unchanged)
- `IOtpSendRateLimiter` / `AllowAllOtpSendRateLimiter` + `OtpOptions.MinSendInterval` (default 30s) / `OtpSendRateLimitedException`
- Access-token `jti` auto-issue; `IAccessTokenRevocationStore` + `IAccessTokenRevocationService`
- Redis lock `ILockHandle.Token` / `FencingToken` / `TryExtendAsync`; `DistributedLockAcquireOptions` renew watchdog
- `SmsTransport.RestV2` — NetGSM POST + Basic Auth + JSON (`SmsOptions.DefaultRestV2ApiUrl`)
- `NetGsmRejectedException` for permanent NetGSM business failures
- `samples/Pinqponq.Playground`: a browser test console that exercises all 13 packages
  against real dependencies, provisions the backing services on demand via Testcontainers,
  and shows the structured log records each run produces alongside its result

### Changed

- Package version **0.2.1**
- Packages target **.NET 8 and every release after it**: `net8.0`, `net9.0` and `net10.0`.
  A .NET 10 application now links an assembly built for its own framework instead of the
  `net8.0` asset. The list is declared once as `PinqponqTargetFrameworks` in
  `Directory.Build.props`; the 27 project files reference the property, so the next runtime
  is a one-line change.
- `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` versions are pinned per target
  framework (8.0.x / 9.0.x / 10.0.x) rather than at 8.0.x for all of them. Everything that
  versions independently of the runtime stays on a single version, which is the point of
  Central Package Management.
- `LangVersion` is pinned per target framework (C# 12 for `net8.0`, 13 for `net9.0`).
  Because every framework compiles the same source, the oldest is the real language
  ceiling; pinning makes the build produce the same result on any SDK. With `latest`, the
  `net8.0` leg was silently compiled by whatever newer compiler was installed — which is
  exactly how the CS0121 break below reached master unnoticed.
- `MongoDB.Driver` 3.6.0 → 3.10.0. The old version pulled `SharpCompress` 0.30.1
  (GHSA-6c8g-7p36-r338, moderate) and `Snappier` 1.0.0 (GHSA-pggp-6c3x-2xmx, high). From
  .NET 9 the SDK audits transitive packages by default, so these failed the build under
  `TreatWarningsAsErrors`; 3.10.0 is the driver's own fix.
- CI installs the .NET 10 SDK alongside 8 and 9. The newest SDK builds every target; the
  older entries supply the runtimes `dotnet test` needs for those legs.

### Fixed

- `IJwtTokenValidator` / `ITotpService` registered as scoped (avoids captive dependency on scoped stores)
- Redis fencing token issued atomically with lock acquire (Lua SET NX + INCR)
- NetGSM business-error bodies are not Polly-retried
- `AccessTokenRevocationService` validates token signature before recording `jti`
- Test projects carrying the ASP.NET Core `FrameworkReference` no longer also reference
  `Microsoft.Extensions.DependencyInjection` / `.Logging` as packages — the shared framework
  provides both, and from .NET 10 the redundant reference is reported as NU1510.
- `AddPinqponqIdentity` no longer aborts host startup in Development. ASP.NET Core
  validates every type-registered descriptor when it builds the container there, so
  registering `RefreshTokenService` by implementation type failed the whole application
  ("Some services are not able to be constructed") for consumers that use the package for
  JWTs and password hashing without ever issuing a refresh token. The missing
  `IRefreshTokenStore` is now reported when `IRefreshTokenService` is first resolved, and
  names the registration to add.
- `AddPinqponqOtp` likewise no longer aborts startup when `IOtpStore` is registered after
  it — or not at all.
- `OtpService` now needs a sender only for the channel it actually delivers to: an
  email-only application no longer has to register an `ISmsSender`. `ISmsSender` and
  `IEmailSender` became optional constructor parameters, and routing a code to a channel
  whose sender is missing throws there, naming `AddPinqponqSms` / `AddPinqponqMail`.
- `AddPinqponqOtp()` and `AddPinqponqTotp()` register their options services even when
  called without a configure action; previously `IOtpService` / `ITotpService` could not
  be resolved from a service collection that nothing else had added options to.
- `Pinqponq.Mail` and the Playground build on the .NET 8 SDK again. Two untyped
  collection-expression arguments made `string.Split` and `Encoding.GetBytes` ambiguous
  under the C# 12 compiler (CS0121); CI only caught net9.0 because it selects the newest
  installed SDK for both target frameworks.

## [0.2.0] - 2026-07-25

### Added

- `IRefreshTokenStore.CompleteRotationAsync` for atomic replacement persist + `ReplacedBy` link
- `IRefreshTokenStore.TryRevokeActiveAsync` / `RevokeAllForSubjectAsync`
- `RefreshTokenOptions.ReuseDetectionGrace` (default 5s)
- `IOtpStore.TryConsumeAsync` / `TryRemoveAsync`
- `OtpOptions.HashPepper` (required) for HMAC-SHA256 code hashing
- `IValidateOptions` + `ValidateOnStart` across Jwt, RefreshToken, Otp, Totp, Redis, RabbitMq, Google, Sms, Mail, Mssql, Postgres, Mongo
- `GoogleAuthOptions.HostedDomain` / `RequireEmailVerified` / `RequireNonce`
- RabbitMQ publisher confirms + `mandatory: true`; consumer confirms for republish
- `RabbitMqConsumerOptions.MaxRedeliveryCount`; `RabbitMqOptions.UseSsl` / `SslServerName`
- `SmsOptions.AllowNoOp` / `HttpTimeout`; `SmtpOptions.AttachmentRoot`
- CI `dotnet pack` + nupkg artifact
- Unit and Testcontainers integration tests for all packages

### Fixed

- Rotate crash window no longer leaves revoked tokens without a replacement link
- Concurrent refresh rotate / OTP verify races closed
- Mail attachments require `AttachmentRoot` (path jail); traversal rejected
- Rabbit republish failures drop instead of infinite requeue; drain timeout nacks remaining
- SMS HTTPS enforced; caller cancel not retried; `AllowNoOp` defaults to false
- Redis empty-string round-trip; Postgres health uses shared `NpgsqlDataSource`
- ErrorHandling non-abort cancellation → 504; Google unverified email → generic error
- JWT RSA private key ≥ 2048 bits

### Changed

- Package version **0.2.0** (breaking store/options API accumulation from 0.1.x)
- `ErrorHandlingOptions.StatusCodeResolver` → `StatusMappingResolver`
- Mongo `RetryCount` / `RetryBaseDelay` removed
- `SmsOptions.AllowNoOp` default is now `false`

## [0.1.0] - 2026-07-25

### Added

- Initial monorepo release of `Pinqponq.*` infrastructure NuGet packages
  (Identity, OTP, TOTP, SSO, Cache, Sms, Mail, Database, RabbitMQ, ErrorHandling)

[1.0.2]: https://github.com/pinqponq/pinqnuqets/releases/tag/v1.0.2
[1.0.1]: https://github.com/pinqponq/pinqnuqets/releases/tag/v1.0.1
[1.0.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v1.0.0
[0.2.1]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.2.1
[0.2.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.2.0
[0.1.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.1.0
