# Pinqponq.Database.Mongo

A minimal MongoDB connection layer for .NET: registers a shared `IMongoClient`
and the default `IMongoDatabase` resolved from configuration, plus a
ready-made `ping` health check. Like the other `Pinqponq.Database.*`
packages, it stops at connectivity — no repository base classes, no document
mapping conventions, no query helpers.

## Install

```bash
dotnet add package Pinqponq.Database.Mongo
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A reachable MongoDB server
- `MongoDB.Driver` (pulled in transitively)

## Quick start

```csharp
using MongoDB.Driver;
using Pinqponq.Database.Mongo.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqMongo(mongo =>
{
    mongo.ConnectionString = builder.Configuration.GetConnectionString("Mongo")!;
    mongo.DatabaseName = "myapp";
});

builder.Services.AddHealthChecks().AddPinqponqMongo();

var app = builder.Build();

app.MapGet("/users/{id}", async (string id, IMongoDatabase database, CancellationToken ct) =>
{
    var collection = database.GetCollection<MongoDB.Bson.BsonDocument>("users");
    var filter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", id);
    var document = await collection.Find(filter).FirstOrDefaultAsync(ct);
    return document is null ? Results.NotFound() : Results.Ok(document.ToJson());
});

app.Run();
```

Using the client or database from an injected service:

```csharp
public sealed class AuditRepository(IMongoDatabase database)
{
    public Task InsertAsync(BsonDocument entry, CancellationToken ct) =>
        database.GetCollection<BsonDocument>("audit-log").InsertOneAsync(entry, cancellationToken: ct);
}
```

## Configuration

`MongoOptions`, configured through the `Action<MongoOptions>` delegate passed
to `AddPinqponqMongo`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | `""` (required) | Standard MongoDB connection string (`mongodb://...` or `mongodb+srv://...`). |
| `DatabaseName` | `string` | `""` (required) | The database resolved into the registered `IMongoDatabase`. |

Options are validated at startup via `ValidateOnStart()` — both fields are
required; a missing `DatabaseName` fails fast rather than surfacing later as
a confusing "database not found" error.

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqMongo(Action<MongoOptions>)` | DI extension (`IServiceCollection`) | Registers a shared `IMongoClient` and the `IMongoDatabase` resolved from `DatabaseName`. |
| `AddPinqponqMongo(...)` | DI extension (`IHealthChecksBuilder`) | Adds a MongoDB `ping` command health check (name defaults to `"mongodb"`). |
| `MongoOptions` | Options | Connection string and default database name. |
| `MongoHealthCheck` | Implementation | Registered by the health check extension; not usually referenced directly. |

## Notes / behavior

- **Two singletons, one client.** `AddPinqponqMongo` registers `IMongoClient`
  as a singleton built from `ConnectionString`, and `IMongoDatabase` as a
  second singleton resolved via `client.GetDatabase(DatabaseName)` — both use
  `TryAddSingleton`, so an app-provided registration for either interface
  takes precedence if added first.
- **Health check is a real `ping`.** `MongoHealthCheck` issues a single
  `{ ping: 1 }` command against the resolved database via
  `RunCommandAsync<BsonDocument>` — it exercises the actual server round
  trip, not just client-side connection state.
- **No built-in retry policy on the connection layer itself.** The MongoDB
  .NET driver has its own internal retry/reconnection behavior (retryable
  reads/writes, server discovery); this package does not layer an additional
  Polly pipeline on top of it, unlike `Pinqponq.Database.Postgres` and
  `Pinqponq.Database.Mssql`.
- **`RetryCount` was removed.** An earlier revision of this package exposed a
  `RetryCount` option on `MongoOptions`; it has since been removed because it
  did not map cleanly onto the driver's own retry semantics. If you're
  upgrading from that version, drop the `RetryCount` assignment — it no
  longer exists.
- **No repositories, no entities.** This package only resolves a client and a
  database handle. Collection access, document shape, and any ODM/mapping
  layer are the consuming application's responsibility.

## Related packages

- [Pinqponq.Database.Postgres](../Pinqponq.Database.Postgres/README.md)
- [Pinqponq.Database.Mssql](../Pinqponq.Database.Mssql/README.md)
- [Pinqponq.Cache](../Pinqponq.Cache/README.md)
- [Pinqponq.Messaging.RabbitMq](../Pinqponq.Messaging.RabbitMq/README.md)

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
