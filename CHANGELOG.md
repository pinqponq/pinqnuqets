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

### Fixed

- `IJwtTokenValidator` / `ITotpService` registered as scoped (avoids captive dependency on scoped stores)
- Redis fencing token issued atomically with lock acquire (Lua SET NX + INCR)
- NetGSM business-error bodies are not Polly-retried
- `AccessTokenRevocationService` validates token signature before recording `jti`

### Changed

- Package version **0.2.1**

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
