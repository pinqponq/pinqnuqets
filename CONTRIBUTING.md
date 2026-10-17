# Contributing to PinqNugets

Thanks for your interest in contributing. This repository packages shared .NET infrastructure as `Pinqponq.*` NuGet libraries.

## Prerequisites

- The [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) SDK — it builds every target framework on its own. The [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) and [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) runtimes are needed to *run* the tests on those targets.
- [Docker](https://docs.docker.com/get-docker/) (Desktop or Engine) for integration tests that use Testcontainers (Redis, RabbitMQ, PostgreSQL, MongoDB, SQL Server, MailHog)

## Development workflow

1. Fork and clone the repository.
2. Create a feature branch from `main` (or `master`).
3. Make focused changes; keep product-specific business logic out of these packages (see "Scope of the packages" below).
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
- Prefer the existing style: nullable enabled, warnings as errors, xUnit with its own `Assert`, hand-rolled fakes over Moq.
- Mark Testcontainers tests with `[Trait("Category", "Integration")]`.
- Dependency versions belong in `Directory.Packages.props` (Central Package Management).
- Do not commit secrets, credentials, or personal NuGet source configs.
- Update `CHANGELOG.md` under `[Unreleased]` when behavior or public API changes.
- Every package is versioned on its own: when you change a package, raise the `<Version>` in that package's `.csproj` (Semantic Versioning). There is no shared version.

## Scope of the packages

These packages hold only code that every Pinqponq ASP.NET Core API needs in the same form. A product-specific thing never goes into them. The same rule is in `CLAUDE.md`, which automated task runners (and other AI agents) read first.

- Test every type, option, default, constant and message: would a product other than the one this code came from need exactly this? If the answer depends on one product, it stays in that product.
- Product-specific means: a header, query key, route, response code, message or default taken from one product's client or contract; an option that exists for one product only; a feature that only products with one kind of client (for example a mobile app) need.
- If you are not sure a part is shared, leave it in the product. You may instead move it into its own opt-in package as a setting that is off by default, with a default that was not copied from a product.
- Technology-specific code (PostgreSQL, MongoDB, EF Core, Vault) is welcome in its own opt-in package, as long as nothing in it names a product.
- Say in the pull request, for each package, what the package kept, what was left in the product, and why.
- Reviewers: a product-specific constant or default in a `pinqnuqets` change is a blocker.

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
