# Pinqponq.Cache

A thin Redis wrapper for .NET: a string/object cache (`ICacheService`) and a
distributed lock (`IDistributedLock`) over a single shared `StackExchange.Redis`
connection multiplexer, plus a ready-made health check. It does not bring in a
second Redis client version or a competing serializer — one connection, one
JSON serializer (`System.Text.Json`), one set of options.

## Install

```bash
dotnet add package Pinqponq.Cache
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A reachable Redis server (standalone, Sentinel, or cluster — anything
  `StackExchange.Redis` can parse a connection string for)

## Quick start

```csharp
using Pinqponq.Cache;
using Pinqponq.Cache.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqCache(redis =>
{
    redis.ConnectionString = "localhost:6379,abortConnect=false";
    redis.InstanceName = "myapp:";
    redis.DefaultTtl = TimeSpan.FromMinutes(10);
});

builder.Services.AddHealthChecks().AddPinqponqRedis();

var app = builder.Build();

app.MapGet("/cache/{key}", async (string key, ICacheService cache) =>
    await cache.GetStringAsync(key) is { } value ? Results.Ok(value) : Results.NotFound());

app.Run();
```

Using the cache and the lock from injected services:

```csharp
public sealed class OrderService(ICacheService cache, IDistributedLock locks)
{
    public async Task<Order?> GetOrderAsync(string orderId, CancellationToken ct)
    {
        var cached = await cache.GetAsync<Order>($"order:{orderId}", ct);
        if (cached is not null)
        {
            return cached;
        }

        await using var handle = await locks.AcquireAsync($"order-load:{orderId}", TimeSpan.FromSeconds(30), ct);
        if (!handle.Acquired)
        {
            throw new InvalidOperationException("Could not acquire the load lock.");
        }

        var order = await LoadFromSourceAsync(orderId, ct);
        await cache.SetAsync($"order:{orderId}", order, TimeSpan.FromMinutes(5), ct);
        return order;
    }

    private Task<Order?> LoadFromSourceAsync(string orderId, CancellationToken ct) => Task.FromResult<Order?>(null);
}

public sealed record Order(string Id);
```

## Configuration

`RedisOptions`, configured through the `Action<RedisOptions>` delegate passed
to `AddPinqponqCache`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | `""` (required) | `StackExchange.Redis` connection string, e.g. `host:6379,password=...,defaultDatabase=0,abortConnect=false`. |
| `InstanceName` | `string?` | `null` | Optional prefix applied to every cache key and to internal lock keys. Useful for multi-tenant or multi-app Redis instances. |
| `DefaultTtl` | `TimeSpan?` | `null` (no expiry) | Applied when a `Set*Async` call omits an explicit `expiry`. Must be positive when set. |

Options are validated at startup via `ValidateOnStart()` — an empty
`ConnectionString` or a non-positive `DefaultTtl` fails fast instead of
surfacing later as a runtime error.

> `abortConnect=false` is strongly recommended in the connection string.
> Without it, `ConnectionMultiplexer.ConnectAsync` can block until the
> connection attempt times out when Redis isn't reachable yet.

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqCache(Action<RedisOptions>)` | DI extension (`IServiceCollection`) | Registers a shared `IConnectionMultiplexer`, `ICacheService`, and `IDistributedLock`. |
| `AddPinqponqRedis(...)` | DI extension (`IHealthChecksBuilder`) | Adds a Redis `PING` health check (name defaults to `"redis"`). |
| `ICacheService` | Interface | `GetStringAsync` / `SetStringAsync`, `GetAsync<T>` / `SetAsync<T>` (JSON via `System.Text.Json`), `RemoveAsync`, `ExistsAsync`. |
| `IDistributedLock` | Interface | `AcquireAsync(resource, expiry, [options], ct)` → `ILockHandle`. |
| `ILockHandle` | Interface (`IAsyncDisposable`) | `Acquired`, `Token`, `FencingToken`, `TryExtendAsync(...)`; disposing releases the lock if held. |
| `DistributedLockAcquireOptions` | Options | `IssueFencingToken` (default `true`), `RenewInterval` (watchdog interval; `null` disables it). |
| `RedisOptions` | Options | Connection string, key prefix, default TTL. |
| `RedisCacheService`, `RedisDistributedLock`, `RedisHealthCheck` | Implementations | Registered by `AddPinqponqCache` / `AddPinqponqRedis`; not usually referenced directly. |

## Notes / behavior

- **Single connection.** `AddPinqponqCache` registers exactly one
  `IConnectionMultiplexer` singleton (`TryAddSingleton`, so an app-provided
  registration wins if added first) and reuses it for the cache and the lock.
- **JSON serialization.** `GetAsync<T>` / `SetAsync<T>` use
  `System.Text.Json` with default options. A payload that fails to
  deserialize as `T` (e.g. schema drift) returns `default` from `GetAsync<T>`
  rather than throwing.
- **Key prefixing.** When `InstanceName` is set, cache keys become
  `{InstanceName}{key}` and lock keys become `{InstanceName}lock:{resource}`
  (with a `:fence` suffix for the fencing counter).
- **Locking is best-effort, not linearizable.** `IDistributedLock` uses Redis
  `SET NX PX` (optionally combined with an atomic `INCR` for the fencing
  token via a Lua script) and `LockRelease`/`LockExtend` guarded by a
  per-acquisition ownership token, so only the holder can release or extend
  it. This is adequate for reducing duplicate work under normal operation,
  not a substitute for a consensus-based lock (e.g. Redlock across multiple
  independent Redis nodes) when correctness under partition is critical.
- **Fencing tokens are advisory.** `DistributedLockAcquireOptions.IssueFencingToken`
  (default `true`) mints a monotonically increasing `long` via Redis `INCR`
  on successful acquire, exposed as `ILockHandle.FencingToken`. **This package
  only mints and stores the token — enforcing it (e.g. rejecting a database
  write whose fencing token is stale) is entirely the caller's
  responsibility.** Set it on your resource records and check it before
  applying a write to get real protection against a lock holder that
  outlives its TTL (e.g. after a long GC pause).
- **Renew watchdog.** Setting `DistributedLockAcquireOptions.RenewInterval` to
  a positive `TimeSpan` starts a background timer that calls
  `TryExtendAsync` on that cadence until the handle is disposed; if a renew
  fails the watchdog logs a warning and stops (the lock will then expire on
  its own TTL). Leaving it `null` (the default) disables the watchdog — you
  are responsible for extending long-running work yourself via
  `TryExtendAsync`.
- **Disposal always attempts release.** `ILockHandle.DisposeAsync` stops the
  watchdog (if any) first, then best-effort releases the lock; a
  `RedisException` during release is swallowed since the lock may have
  already expired or the connection may have dropped.
- **No repositories, no entities.** This package is transport-only: it has no
  opinion on what you store or how you model it.

## Related packages

- [Pinqponq.Database.Postgres](../Pinqponq.Database.Postgres/README.md)
- [Pinqponq.Database.Mongo](../Pinqponq.Database.Mongo/README.md)
- [Pinqponq.Database.Mssql](../Pinqponq.Database.Mssql/README.md)
- [Pinqponq.Messaging.RabbitMq](../Pinqponq.Messaging.RabbitMq/README.md)

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
