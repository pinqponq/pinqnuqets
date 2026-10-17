# Pinqponq.Database.Postgres.EntityFrameworkCore

Registers an EF Core `DbContext` on PostgreSQL (Npgsql) in one call, with one shared
`NpgsqlDataSource` (connection pool) per context and a hook to configure that data source.
Entities, configurations and migrations stay in the application.

Services that use Npgsql without EF Core take [Pinqponq.Database.Postgres](../Pinqponq.Database.Postgres/README.md)
instead; this package keeps the EF Core dependency out of them.

## Install

```bash
dotnet add package Pinqponq.Database.Postgres.EntityFrameworkCore
```

## Quick start

```csharp
using Pinqponq.Database.Postgres.EntityFrameworkCore.DependencyInjection;

builder.Services.AddPinqponqPostgresDbContext<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSql"),
    dataSource => dataSource.EnableDynamicJson(
        jsonbClrTypes: [typeof(Dictionary<string, string>)],
        jsonClrTypes: []));
```

| Parameter | Notes |
|---|---|
| `connectionString` | Required. A missing or blank value throws `InvalidOperationException` at registration, so the service fails at startup. |
| `configureDataSource` | Optional `Action<NpgsqlDataSourceBuilder>`: type mappings, dynamic JSON, logging. |
| `configureNpgsql` | Optional `Action<NpgsqlDbContextOptionsBuilder>`: migrations assembly, retry on failure, … |

## Behavior

- The data source is built once, on first use, and registered as the singleton
  `PostgresDbContextDataSource<TDbContext>`; the container disposes it on shutdown. Each
  context type has its own, so two contexts with different connection strings or data
  source settings do not share a pool.
- The context itself is registered with `AddDbContext` (scoped), as EF Core does by default.

## Dependencies

`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.10 or later (`Microsoft.EntityFrameworkCore` 8.0.31,
`Npgsql` 8.0.5). An application on EF Core 9 or 10 references its own provider version and
resolves to it.

## Repository

https://github.com/pinqponq/pinqnuqets
