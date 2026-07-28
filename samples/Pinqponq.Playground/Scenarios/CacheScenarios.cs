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
        yield return ObjectRoundTrip();
        yield return LockContention();
        yield return HealthCheck();
        yield return UnreachableHealthCheck();
    }

    private static Scenario StringRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "cache.string",
            PackageId = Package,
            Title = "String yaz, oku, sil",
            Summary = "SetStringAsync / GetStringAsync / ExistsAsync / RemoveAsync döngüsü ve "
                      + "InstanceName önekinin anahtara nasıl yansıdığı.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("key", "Anahtar", ScenarioFieldKind.Text, "playground:selam"),
                new ScenarioField("value", "Değer", ScenarioFieldKind.Text, "merhaba dünya"),
                new ScenarioField("instanceName", "InstanceName (önek)", ScenarioFieldKind.Text, "pg:"),
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
            context.Step("Değer yazıldı");

            var read = await cache.GetStringAsync(key, context.CancellationToken);
            context.Require("Okunan değer aynı", read == value, read);

            context.Require("ExistsAsync true", await cache.ExistsAsync(key, context.CancellationToken));

            context.Require("RemoveAsync true", await cache.RemoveAsync(key, context.CancellationToken));
            context.Require("Silindikten sonra null", await cache.GetStringAsync(key, context.CancellationToken) is null);

            context.Artifact("redis'teki gerçek anahtar", $"{context.Input.TextOrNull("instanceName")}{key}", "text");
        });

    private static Scenario ObjectRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "cache.object",
            PackageId = Package,
            Title = "Nesne yaz ve oku (JSON)",
            Summary = "SetAsync<T>/GetAsync<T> nesneyi System.Text.Json ile serileştirir. "
                      + "Redis'te tutulan ham JSON da gösterilir.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("key", "Anahtar", ScenarioFieldKind.Text, "playground:kullanici"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqCache(redis =>
                redis.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Redis)));

            var cache = host.GetRequiredService<ICacheService>();
            var key = context.Input.Text("key");
            var user = new CachedUser("user-42", "Test Kullanıcı", ["admin", "editor"], DateTimeOffset.UtcNow);

            await cache.SetAsync(key, user, TimeSpan.FromMinutes(5), context.CancellationToken);
            context.Step("Nesne yazıldı");

            var raw = await cache.GetStringAsync(key, context.CancellationToken);
            context.Artifact("redis'teki ham değer", raw, "text");

            var read = await cache.GetAsync<CachedUser>(key, context.CancellationToken);
            context.Require("Nesne geri okundu", read is not null);
            context.Require("Id korunmuş", read!.Id == user.Id);
            context.Require("Roller korunmuş", read.Roles.SequenceEqual(user.Roles));
            context.Artifact("okunan nesne", read);

            var missing = await cache.GetAsync<CachedUser>("playground:yok-boyle-bir-anahtar", context.CancellationToken);
            context.Require("Olmayan anahtar null döner", missing is null);

            await cache.RemoveAsync(key, context.CancellationToken);
        });

    private static Scenario LockContention() => new(
        new ScenarioDescriptor
        {
            Id = "cache.lock",
            PackageId = Package,
            Title = "Dağıtık kilit çekişmesi",
            Summary = "Aynı kaynağı iki ayrı DI konteynerinden kilitlemeye çalışır. İkincisi "
                      + "Acquired=false alır; birincisi bırakınca üçüncü deneme başarılı olur.",
            RequiredServices = [DevServiceIds.Redis],
            Fields =
            [
                new ScenarioField("resource", "Kaynak adı", ScenarioFieldKind.Text, "siparis-1234"),
                new ScenarioField("expiryMs", "Kilit süresi (ms)", ScenarioFieldKind.Duration, "30000"),
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
            context.Require("Birinci istemci kilidi aldı", firstHandle.Acquired);

            var secondHandle = await second.GetRequiredService<IDistributedLock>()
                .AcquireAsync(resource, expiry, context.CancellationToken);
            context.Require("İkinci istemci kilidi alamadı", !secondHandle.Acquired);
            await secondHandle.DisposeAsync();

            await firstHandle.DisposeAsync();
            context.Step("Birinci istemci kilidi bıraktı");

            var thirdHandle = await second.GetRequiredService<IDistributedLock>()
                .AcquireAsync(resource, expiry, context.CancellationToken);
            context.Require("Bırakıldıktan sonra yeniden alınabildi", thirdHandle.Acquired);
            await thirdHandle.DisposeAsync();

            context.Artifact("sonuç", new
            {
                birinci = true,
                ikinciEszamanli = false,
                birakildiktanSonra = true,
                redisAnahtari = $"lock:{resource}",
            });
        });

    private static Scenario HealthCheck() => new(
        new ScenarioDescriptor
        {
            Id = "cache.health",
            PackageId = Package,
            Title = "Redis health-check (sağlıklı)",
            Summary = "Paketin AddPinqponqRedis health-check'ini çalıştırır ve raporu gösterir.",
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

            context.Require("Genel durum Healthy", report.Status == HealthStatus.Healthy, report.Status.ToString());
            context.Artifact("rapor", Project(report));
        });

    private static Scenario UnreachableHealthCheck() => new(
        new ScenarioDescriptor
        {
            Id = "cache.health-unreachable",
            PackageId = Package,
            Title = "Redis health-check (erişilemiyor)",
            Summary = "Kapalı bir porta bağlanmayı dener. abortConnect=false verilmesi şart: "
                      + "aksi hâlde ConnectionMultiplexer.Connect bağlantı zaman aşımına kadar bloklar.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("connectionString", "Bağlantı dizesi", ScenarioFieldKind.Text,
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

            context.Require("Durum Unhealthy", report.Status == HealthStatus.Unhealthy, report.Status.ToString());
            context.Artifact("rapor", Project(report));
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
