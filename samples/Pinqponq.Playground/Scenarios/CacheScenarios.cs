using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Cache;
using Pinqponq.Cache.DependencyInjection;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Cache</c>. All of them need the Redis container.</summary>
public static class CacheScenarios
{
    private const string Package = "Pinqponq.Cache";

    public static IEnumerable<Scenario> Create()
    {
        yield return StringRoundTrip();
        yield return EmptyStringRoundTrip();
        yield return ObjectRoundTrip();
        yield return LockContention();
        yield return LockFencingAndExtend();
        yield return HealthCheck();
        yield return UnreachableHealthCheck();
    }

    private static Scenario StringRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "cache.string",
            PackageId = Package,
            Title = "Write, read, delete a string",
            Summary = "The SetStringAsync / GetStringAsync / ExistsAsync / RemoveAsync cycle, and how "
                      + "the InstanceName prefix is reflected in the key.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("key", "Key", ScenarioFieldKind.Text, "playground:hello"),
                new ScenarioField("value", "Value", ScenarioFieldKind.Text, "hello world"),
                new ScenarioField("instanceName", "InstanceName (prefix)", ScenarioFieldKind.Text, "pg:"),
                new ScenarioField("ttlMs", "TTL (ms)", ScenarioFieldKind.Duration, "60000"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqCache(redis =>
            {
                redis.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis);
                redis.InstanceName = context.Input.TextOrNull("instanceName");
            }));

            var cache = host.GetRequiredService<ICacheService>();
            var key = context.Input.Text("key");
            var value = context.Input.Text("value");

            await cache.SetStringAsync(key, value, context.Input.Duration("ttlMs"), context.CancellationToken);
            context.Step("Value written");

            var read = await cache.GetStringAsync(key, context.CancellationToken);
            context.Require("Value read back matches", read == value, read);

            context.Require("ExistsAsync true", await cache.ExistsAsync(key, context.CancellationToken));

            context.Require("RemoveAsync true", await cache.RemoveAsync(key, context.CancellationToken));
            context.Require("Null after deletion", await cache.GetStringAsync(key, context.CancellationToken) is null);

            context.Artifact("actual key in redis", $"{context.Input.TextOrNull("instanceName")}{key}", "text");
        });

    private static Scenario EmptyStringRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "cache.empty-string",
            PackageId = Package,
            Title = "Empty string round-trip",
            Summary = "0.2.0 fix: an empty string can be written and read back — it's not confused with null.",
            RequiredServices = [DevServiceIds.Redis],
            Fields = [new ScenarioField("key", "Key", ScenarioFieldKind.Text, "playground:empty")],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqCache(redis =>
                redis.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis)));

            var cache = host.GetRequiredService<ICacheService>();
            var key = context.Input.Text("key");

            await cache.SetStringAsync(key, string.Empty, TimeSpan.FromMinutes(1), context.CancellationToken);
            var read = await cache.GetStringAsync(key, context.CancellationToken);
            context.Require("Empty string came back (not null)", read == string.Empty, read is null ? "(null)" : $"'{read}'");
            await cache.RemoveAsync(key, context.CancellationToken);
        });

    private static Scenario ObjectRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "cache.object",
            PackageId = Package,
            Title = "Write and read an object (JSON)",
            Summary = "SetAsync<T>/GetAsync<T> serializes the object with System.Text.Json. The raw "
                      + "JSON kept in Redis is also shown.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("key", "Key", ScenarioFieldKind.Text, "playground:user"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqCache(redis =>
                redis.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis)));

            var cache = host.GetRequiredService<ICacheService>();
            var key = context.Input.Text("key");
            var user = new CachedUser("user-42", "Test User", ["admin", "editor"], DateTimeOffset.UtcNow);

            await cache.SetAsync(key, user, TimeSpan.FromMinutes(5), context.CancellationToken);
            context.Step("Object written");

            var raw = await cache.GetStringAsync(key, context.CancellationToken);
            context.Artifact("raw value in redis", raw, "text");

            var read = await cache.GetAsync<CachedUser>(key, context.CancellationToken);
            context.Require("Object read back", read is not null);
            context.Require("Id preserved", read!.Id == user.Id);
            context.Require("Roles preserved", read.Roles.SequenceEqual(user.Roles));
            context.Artifact("object read back", read);

            var missing = await cache.GetAsync<CachedUser>("playground:no-such-key", context.CancellationToken);
            context.Require("A missing key returns null", missing is null);

            await cache.RemoveAsync(key, context.CancellationToken);
        });

    private static Scenario LockContention() => new(
        new ScenarioDescriptor
        {
            Id = "cache.lock",
            PackageId = Package,
            Title = "Distributed lock contention",
            Summary = "Attempts to lock the same resource from two separate DI containers. The "
                      + "second gets Acquired=false; once the first releases, the third attempt succeeds.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("resource", "Resource name", ScenarioFieldKind.Text, "order-1234"),
                new ScenarioField("expiryMs", "Lock duration (ms)", ScenarioFieldKind.Duration, "30000"),
            ],
        },
        async context =>
        {
            var connectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis);
            var resource = context.Input.Text("resource");
            var expiry = context.Input.Duration("expiryMs");

            await using var first = context.Host(services =>
                services.AddPinqponqCache(redis => redis.ConnectionString = connectionString));
            await using var second = context.Host(services =>
                services.AddPinqponqCache(redis => redis.ConnectionString = connectionString));

            var firstHandle = await first.GetRequiredService<IDistributedLock>()
                .AcquireAsync(resource, expiry, context.CancellationToken);
            context.Require("First client acquired the lock", firstHandle.Acquired);

            var secondHandle = await second.GetRequiredService<IDistributedLock>()
                .AcquireAsync(resource, expiry, context.CancellationToken);
            context.Require("Second client could not acquire the lock", !secondHandle.Acquired);
            await secondHandle.DisposeAsync();

            await firstHandle.DisposeAsync();
            context.Step("First client released the lock");

            var thirdHandle = await second.GetRequiredService<IDistributedLock>()
                .AcquireAsync(resource, expiry, context.CancellationToken);
            context.Require("Acquired again after release", thirdHandle.Acquired);
            await thirdHandle.DisposeAsync();

            context.Artifact("result", new
            {
                first = true,
                secondConcurrent = false,
                afterRelease = true,
                redisKey = $"lock:{resource}",
            });
        });

    private static Scenario LockFencingAndExtend() => new(
        new ScenarioDescriptor
        {
            Id = "cache.lock-fencing",
            PackageId = Package,
            Title = "Fencing token and TryExtendAsync",
            Summary = "An atomic fencing token is generated when the lock is acquired; TryExtendAsync "
                      + "extends the TTL using the ownership token.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("resource", "Resource name", ScenarioFieldKind.Text, "fence-demo"),
                new ScenarioField("expiryMs", "Lock duration (ms)", ScenarioFieldKind.Duration, "5000"),
            ],
        },
        async context =>
        {
            var connectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis);
            var resource = context.Input.Text("resource");
            var expiry = context.Input.Duration("expiryMs");

            await using var host = context.Host(services =>
                services.AddPinqponqCache(redis => redis.ConnectionString = connectionString));

            await using var handle = await host.GetRequiredService<IDistributedLock>()
                .AcquireAsync(
                    resource,
                    expiry,
                    new DistributedLockAcquireOptions { IssueFencingToken = true },
                    context.CancellationToken);

            context.Require("Lock acquired", handle.Acquired);
            context.Require("FencingToken generated", handle.FencingToken is not null, handle.FencingToken?.ToString());
            context.Require("Ownership Token is present", !string.IsNullOrEmpty(handle.Token));

            var extended = await handle.TryExtendAsync(expiry, context.CancellationToken);
            context.Require("TryExtendAsync true", extended);

            context.Artifact("lock", new
            {
                handle.Acquired,
                handle.FencingToken,
                token = handle.Token,
                extended,
            });
        });

    private static Scenario HealthCheck() => new(
        new ScenarioDescriptor
        {
            Id = "cache.health",
            PackageId = Package,
            Title = "Redis health-check (healthy)",
            Summary = "Runs the package's AddPinqponqRedis health-check and shows the report.",
            RequiredServices = [DevServiceIds.Redis],
        },
        async context =>
        {
            await using var host = context.Host(services =>
            {
                services.AddPinqponqCache(redis =>
                    redis.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis));
                services.AddHealthChecks().AddPinqponqRedis();
            });

            var report = await host.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);

            context.Require("Overall status is Healthy", report.Status == HealthStatus.Healthy, report.Status.ToString());
            context.Artifact("report", Project(report));
        });

    private static Scenario UnreachableHealthCheck() => new(
        new ScenarioDescriptor
        {
            Id = "cache.health-unreachable",
            PackageId = Package,
            Title = "Redis health-check (unreachable)",
            Summary = "Attempts to connect to a closed port. abortConnect=false must be set: "
                      + "otherwise ConnectionMultiplexer.Connect blocks until the connection times out.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("connectionString", "Connection string", ScenarioFieldKind.Text,
                    "127.0.0.1:6399,abortConnect=false,connectTimeout=500,syncTimeout=500"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services =>
            {
                services.AddPinqponqCache(redis => redis.ConnectionString = context.Input.Text("connectionString"));
                services.AddHealthChecks().AddPinqponqRedis();
            });

            var report = await host.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);

            context.Require("Status is Unhealthy", report.Status == HealthStatus.Unhealthy, report.Status.ToString());
            context.Artifact("report", Project(report));
        });

    private static object Project(HealthReport report) => new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => (object)new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                exceptionType = entry.Value.Exception?.GetType().FullName,
                exceptionMessage = entry.Value.Exception?.Message,
            },
            StringComparer.Ordinal),
    };

    private sealed record CachedUser(string Id, string Name, IReadOnlyList<string> Roles, DateTimeOffset CachedAt);
}
