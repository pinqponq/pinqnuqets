# Contributing to PinqNugets

Thanks for your interest in contributing. This repository packages shared .NET infrastructure as `Pinqponq.*` NuGet libraries.

## Prerequisites

- [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) and [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) SDKs
- [Docker](https://docs.docker.com/get-docker/) (Desktop or Engine) for integration tests that use Testcontainers (Redis, RabbitMQ, PostgreSQL, MongoDB, SQL Server, MailHog)

## Development workflow

1. Fork and clone the repository.
2. Create a feature branch from `main` (or `master`).
3. Make focused changes; keep project-specific business logic out of these packages.
4. Build and test:

```bash
dotnet build -c Release
dotnet test -c Release --collect:"XPlat Code Coverage"
```

5. Open a pull request using the PR template.

## Guidelines

- Target both `net8.0` and `net9.0`.
- Prefer the existing style: nullable enabled, warnings as errors, FluentAssertions + xUnit, hand-rolled fakes over Moq.
- Mark Testcontainers tests with `[Trait("Category", "Integration")]`.
- Dependency versions belong in `Directory.Packages.props` (Central Package Management).
- Do not commit secrets, credentials, or personal NuGet source configs.
- Update `CHANGELOG.md` under `[Unreleased]` when behavior or public API changes.

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
