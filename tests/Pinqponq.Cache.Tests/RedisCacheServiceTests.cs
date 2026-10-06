using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pinqponq.TestSupport.Fixtures;
using StackExchange.Redis;
using Xunit;

namespace Pinqponq.Cache.Tests;

[Collection(RedisCollection.Name)]
public sealed class RedisCacheServiceTests
{
    private readonly RedisCollectionFixture _fixture;

    public RedisCacheServiceTests(RedisCollectionFixture fixture) => _fixture = fixture;

    private (RedisCacheService cache, IConnectionMultiplexer mux, RedisOptions options) Create(
        string? instanceName = null)
    {
        var options = new RedisOptions
        {
            ConnectionString = _fixture.ConnectionString,
            InstanceName = instanceName,
            DefaultTtl = TimeSpan.FromMinutes(5),
        };
        var mux = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var cache = new RedisCacheService(mux, Options.Create(options));
        return (cache, mux, options);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task String_roundtrip_works()
    {
        var (cache, mux, _) = Create();
        await using (mux)
        {
            var key = $"str-{Guid.NewGuid():N}";
            await cache.SetStringAsync(key, "hello");
            Assert.Equal("hello", (await cache.GetStringAsync(key)));
            Assert.True((await cache.ExistsAsync(key)));
            Assert.True((await cache.RemoveAsync(key)));
            Assert.False((await cache.ExistsAsync(key)));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Json_roundtrip_works()
    {
        var (cache, mux, _) = Create();
        await using (mux)
        {
            var key = $"json-{Guid.NewGuid():N}";
            await cache.SetAsync(key, new Sample { Name = "pinq", Count = 3 });
            var loaded = await cache.GetAsync<Sample>(key);
            Assert.NotNull(loaded);
            Assert.Equal("pinq", loaded!.Name);
            Assert.Equal(3, loaded.Count);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InstanceName_prefixes_keys()
    {
        var prefix = $"pfx-{Guid.NewGuid():N}:";
        var (cache, mux, _) = Create(prefix);
        await using (mux)
        {
            var key = "k1";
            await cache.SetStringAsync(key, "v");
            var db = mux.GetDatabase();
            Assert.Equal("v", (await db.StringGetAsync(prefix + key)).ToString());
            Assert.False((await db.KeyExistsAsync(key)));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Empty_key_throws()
    {
        var (cache, mux, _) = Create();
        await using (mux)
        {
            var act = () => cache.GetStringAsync("");
            await Assert.ThrowsAnyAsync<ArgumentException>(act);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Corrupt_json_returns_default()
    {
        var (cache, mux, _) = Create();
        await using (mux)
        {
            var key = $"bad-{Guid.NewGuid():N}";
            await cache.SetStringAsync(key, "{not-json");
            Assert.Null((await cache.GetAsync<Sample>(key)));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Empty_string_roundtrips()
    {
        var (cache, mux, _) = Create();
        await using (mux)
        {
            var key = $"empty-{Guid.NewGuid():N}";
            await cache.SetStringAsync(key, "");
            Assert.Equal("", (await cache.GetStringAsync(key)));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Distributed_lock_acquire_and_contention()
    {
        var options = Options.Create(new RedisOptions
        {
            ConnectionString = _fixture.ConnectionString,
            InstanceName = $"lock-{Guid.NewGuid():N}:",
        });
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var locks = new RedisDistributedLock(mux, options);
        var resource = "res-1";

        await using var first = await locks.AcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.True(first.Acquired);
        Assert.False(string.IsNullOrWhiteSpace(first.Token));
        Assert.NotNull(first.FencingToken);

        await using var second = await locks.AcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.False(second.Acquired);
        Assert.Null(second.FencingToken);

        await first.DisposeAsync();
        await using var third = await locks.AcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.True(third.Acquired);
        Assert.True(third.FencingToken > first.FencingToken!.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Distributed_lock_TryExtend_succeeds_while_held()
    {
        var options = Options.Create(new RedisOptions
        {
            ConnectionString = _fixture.ConnectionString,
            InstanceName = $"lock-ext-{Guid.NewGuid():N}:",
        });
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var locks = new RedisDistributedLock(mux, options);

        await using var handle = await locks.AcquireAsync("res-ext", TimeSpan.FromSeconds(5));
        Assert.True(handle.Acquired);
        Assert.True((await handle.TryExtendAsync(TimeSpan.FromSeconds(30))));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_check_is_healthy()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var check = new RedisHealthCheck(mux);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private sealed class Sample
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
