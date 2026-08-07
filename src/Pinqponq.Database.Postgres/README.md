# Pinqponq.Database.Postgres

A minimal, opinionated PostgreSQL connection layer for .NET: a shared
`NpgsqlDataSource`, a retrying connection factory (`IPostgresConnectionFactory`),
and a ready-made health check. It deliberately stops at "give me an open
connection" — no repository base classes, no entity mapping, no query
builder. What you do with the connection is up to you (raw ADO.NET, Dapper,
EF Core, etc.).

## Install

```bash
dotnet add package Pinqponq.Database.Postgres
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A reachable PostgreSQL server
- `Npgsql` (pulled in transitively)

## Quick start

```csharp
using Pinqponq.Database.Postgres;
using Pinqponq.Database.Postgres.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqPostgres(postgres =>
{
    postgres.ConnectionString = builder.Configuration.GetConnectionString("Postgres")!;
    postgres.RetryCount = 3;
    postgres.RetryBaseDelay = TimeSpan.FromMilliseconds(200);
});

builder.Services.AddHealthChecks().AddPinqponqPostgres();

var app = builder.Build();

app.MapGet("/version", async (IPostgresConnectionFactory factory, CancellationToken ct) =>
{
    await using var connection = await factory.OpenConnectionAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT version()";
    return await command.ExecuteScalarAsync(ct);
});

app.Run();
```

Using the factory from an injected service:

```csharp
public sealed class ReportRepository(IPostgresConnectionFactory factory)
{
    public async Task<int> CountOrdersAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM orders";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
```

## Configuration

`PostgresOptions`, configured through the `Action<PostgresOptions>` delegate
passed to `AddPinqponqPostgres`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | `""` (required) | Standard Npgsql connection string. |
| `RetryCount` | `int` | `3` | Maximum retry attempts on transient failures. Must not be negative. `0` is valid for the options but note that Polly's retry pipeline itself may reject `MaxRetryAttempts = 0` depending on configuration — prefer `1` or higher for real resiliency. |
| `RetryBaseDelay` | `TimeSpan` | `200ms` | Base delay for exponential backoff between retries (with jitter). Must be positive. |

Options are validated at startup via `ValidateOnStart()`.

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqPostgres(Action<PostgresOptions>)` | DI extension (`IServiceCollection`) | Registers a shared `NpgsqlDataSource` and an `IPostgresConnectionFactory` with retry resiliency. |
| `AddPinqponqPostgres(...)` | DI extension (`IHealthChecksBuilder`) | Adds a `SELECT 1` health check (name defaults to `"postgres"`) that opens its own connection from the shared data source, **bypassing the retry pipeline**. |
| `IPostgresConnectionFactory` | Interface | `OpenConnectionAsync(ct)` → `NpgsqlConnection`, retrying transient failures per `PostgresOptions`. |
| `PostgresOptions` | Options | Connection string and retry configuration. |
| `PostgresConnectionFactory`, `PostgresHealthCheck` | Implementations | Registered by the extension methods; not usually referenced directly. |

## Notes / behavior

- **Shared data source.** `AddPinqponqPostgres` registers a single
  `NpgsqlDataSource` singleton built from `ConnectionString` via
  `NpgsqlDataSource.Create(...)`, and both `IPostgresConnectionFactory` and
  the health check pull connections from it — pooling is handled by Npgsql
  itself, not duplicated by this package.
- **Retry classification.** The factory wraps
  `NpgsqlDataSource.OpenConnectionAsync` in a Polly `ResiliencePipeline` that
  retries `NpgsqlException` when `IsTransient` is `true` and any
  `TimeoutException`, using exponential backoff with jitter. Non-transient
  errors (e.g. bad credentials, syntax errors surfaced during connection
  setup) are not retried and propagate immediately.
- **Health check bypasses retry.** `PostgresHealthCheck` opens a connection
  directly from the shared `NpgsqlDataSource` and runs `SELECT 1` — it does
  **not** go through `IPostgresConnectionFactory`'s retry pipeline, so a
  single failed attempt is enough to report `Unhealthy`. This is intentional:
  health checks should reflect current reachability, not retry-smoothed
  reachability.
- **No repositories, no entities.** This package only opens connections. Data
  access patterns, table mapping, and migrations are entirely up to the
  consuming application.

## Related packages

- [Pinqponq.Database.Mongo](../Pinqponq.Database.Mongo/README.md)
- [Pinqponq.Database.Mssql](../Pinqponq.Database.Mssql/README.md)
- [Pinqponq.Cache](../Pinqponq.Cache/README.md)
- [Pinqponq.Messaging.RabbitMq](../Pinqponq.Messaging.RabbitMq/README.md)

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
