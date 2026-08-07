# Pinqponq.Database.Mssql

A minimal SQL Server connection layer for .NET: a retrying connection factory
(`ISqlConnectionFactory`) built on `Microsoft.Data.SqlClient`, plus a
ready-made health check. Same shape as `Pinqponq.Database.Postgres` — it
opens connections with transient-fault classification and stops there. No
repository base classes, no entity mapping.

## Install

```bash
dotnet add package Pinqponq.Database.Mssql
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A reachable SQL Server instance (or Azure SQL)
- `Microsoft.Data.SqlClient` (pulled in transitively)

## Quick start

```csharp
using Pinqponq.Database.Mssql;
using Pinqponq.Database.Mssql.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqMssql(mssql =>
{
    mssql.ConnectionString = builder.Configuration.GetConnectionString("Mssql")!;
    mssql.RetryCount = 3;
    mssql.RetryBaseDelay = TimeSpan.FromMilliseconds(200);
});

builder.Services.AddHealthChecks().AddPinqponqMssql();

var app = builder.Build();

app.MapGet("/version", async (ISqlConnectionFactory factory, CancellationToken ct) =>
{
    await using var connection = await factory.OpenConnectionAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT @@VERSION";
    return await command.ExecuteScalarAsync(ct);
});

app.Run();
```

Using the factory from an injected service:

```csharp
public sealed class ReportRepository(ISqlConnectionFactory factory)
{
    public async Task<int> CountOrdersAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Orders";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
```

## Configuration

`MssqlOptions`, configured through the `Action<MssqlOptions>` delegate passed
to `AddPinqponqMssql`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | `""` (required) | Standard `Microsoft.Data.SqlClient` connection string. |
| `RetryCount` | `int` | `3` | Maximum retry attempts on transient failures. Must not be negative. |
| `RetryBaseDelay` | `TimeSpan` | `200ms` | Base delay for exponential backoff between retries (with jitter). Must be positive. |

Options are validated at startup via `ValidateOnStart()`.

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqMssql(Action<MssqlOptions>)` | DI extension (`IServiceCollection`) | Registers an `ISqlConnectionFactory` with retry resiliency. |
| `AddPinqponqMssql(...)` | DI extension (`IHealthChecksBuilder`) | Adds a `SELECT 1` health check (name defaults to `"mssql"`) that opens its own connection directly, **bypassing the retry pipeline**. |
| `ISqlConnectionFactory` | Interface | `OpenConnectionAsync(ct)` → `SqlConnection`, retrying transient failures per `MssqlOptions`. |
| `MssqlOptions` | Options | Connection string and retry configuration. |
| `SqlConnectionFactory`, `MssqlHealthCheck` | Implementations | Registered by the extension methods; not usually referenced directly. |

## Notes / behavior

- **Transient error classification.** `SqlConnectionFactory` wraps connection
  opening in a Polly `ResiliencePipeline` that retries on a curated set of
  well-known transient SQL Server error numbers — timeouts, throttling,
  failover, and network-level errors such as `-2`, `20`, `64`, `233`,
  `10053`, `10054`, `10060`, `10928`, `10929`, `40197`, `40501`, `40613`,
  `49918`, `49919`, `49920`, `4060`, and `4221` — plus any `TimeoutException`.
  Errors outside this set (e.g. login failures, invalid object names) are not
  retried and propagate immediately.
- **A fresh `SqlConnection` per attempt.** Each retry attempt constructs a new
  `SqlConnection` and calls `OpenAsync`; a connection that fails to open is
  disposed before the next attempt, so no half-open connections leak across
  retries.
- **Health check bypasses retry.** `MssqlHealthCheck` opens its own
  `SqlConnection` directly from `ConnectionString` and runs `SELECT 1` — it
  does **not** go through `ISqlConnectionFactory`'s retry pipeline, so a
  single failed attempt is enough to report `Unhealthy`.
- **No connection pooling layer of its own.** Connection pooling is handled
  by `Microsoft.Data.SqlClient` itself (ADO.NET connection pooling), not
  duplicated by this package.
- **No repositories, no entities.** This package only opens connections. Data
  access patterns, table mapping, and migrations are entirely up to the
  consuming application.

## Related packages

- [Pinqponq.Database.Postgres](../Pinqponq.Database.Postgres/README.md)
- [Pinqponq.Database.Mongo](../Pinqponq.Database.Mongo/README.md)
- [Pinqponq.Cache](../Pinqponq.Cache/README.md)
- [Pinqponq.Messaging.RabbitMq](../Pinqponq.Messaging.RabbitMq/README.md)

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
