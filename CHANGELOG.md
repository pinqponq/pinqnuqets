# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.2.1]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.2.1
[0.2.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.2.0
[0.1.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.1.0
