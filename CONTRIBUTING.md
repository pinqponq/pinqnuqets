# Contributing to PinqNugets

Thanks for your interest in contributing. This repository packages shared .NET infrastructure as `Pinqponq.*` NuGet libraries.

## Prerequisites

- The [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) SDK — it builds every target framework on its own. The [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) and [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) runtimes are needed to *run* the tests on those targets.
- [Docker](https://docs.docker.com/get-docker/) (Desktop or Engine) for integration tests that use Testcontainers (Redis, RabbitMQ, PostgreSQL, MongoDB, SQL Server, MailHog)

## Development workflow

1. Fork and clone the repository.
2. Create a feature branch from `main` (or `master`).
3. Make focused changes; keep project-specific business logic out of these packages.
4. Build and test:

```bash
dotnet build -c Release
dotnet test -c Release --collect:"XPlat Code Coverage"

# Missing a runtime, or want a quick single-target loop? Override the list:
dotnet build -c Release -p:PinqponqTargetFrameworks=net8.0
dotnet test  -c Release -f net10.0 --filter "Category!=Integration"   # skips Testcontainers
```

5. Open a pull request using the PR template.

## Guidelines

- Target .NET 8 and every release after it. The list lives in one place — `PinqponqTargetFrameworks` in `Directory.Build.props`; projects reference it rather than spelling frameworks out, so adding the next runtime is a single edit.
- `LangVersion` is pinned per target framework, so the oldest one is the real language ceiling. New syntax that the `net8.0` leg rejects fails the build there, whichever SDK you have installed.
- Prefer the existing style: nullable enabled, warnings as errors, FluentAssertions + xUnit, hand-rolled fakes over Moq.
- Mark Testcontainers tests with `[Trait("Category", "Integration")]`.
- Dependency versions belong in `Directory.Packages.props` (Central Package Management).
- Do not commit secrets, credentials, or personal NuGet source configs.
- Update `CHANGELOG.md` under `[Unreleased]` when behavior or public API changes.

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
