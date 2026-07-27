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

## [0.1.0] - 2026-07-25

### Added

- Initial monorepo release of `Pinqponq.*` infrastructure NuGet packages
  (Identity, OTP, TOTP, SSO, Cache, Sms, Mail, Database, RabbitMQ, ErrorHandling)

[Unreleased]: https://github.com/pinqponq/pinqnuqets/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pinqponq/pinqnuqets/releases/tag/v0.1.0
