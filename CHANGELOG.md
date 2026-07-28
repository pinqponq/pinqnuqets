# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Comprehensive unit and Testcontainers integration tests for all packages
- GitHub Actions CI, contribution and security documentation
- `samples/Pinqponq.Playground`: a browser test console that exercises all 13 packages
  against real dependencies, provisions the backing services on demand via Testcontainers,
  and shows the structured log records each run produces alongside its result

### Fixed

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

## [0.1.0] - 2026-07-25

### Added

- Initial monorepo release of `Pinqponq.*` infrastructure NuGet packages
  (Identity, OTP, TOTP, SSO, Cache, Sms, Mail, Database, RabbitMQ, ErrorHandling)

[Unreleased]: https://github.com/pinqponq/pinqnuqets/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.1.0
