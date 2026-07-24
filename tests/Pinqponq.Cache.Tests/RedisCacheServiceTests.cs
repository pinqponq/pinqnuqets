using FluentAssertions;
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
            (await cache.GetStringAsync(key)).Should().Be("hello");
            (await cache.ExistsAsync(key)).Should().BeTrue();
            (await cache.RemoveAsync(key)).Should().BeTrue();
            (await cache.ExistsAsync(key)).Should().BeFalse();
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
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("pinq");
            loaded.Count.Should().Be(3);
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
            (await db.StringGetAsync(prefix + key)).ToString().Should().Be("v");
            (await db.KeyExistsAsync(key)).Should().BeFalse();
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
            await act.Should().ThrowAsync<ArgumentException>();
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
        first.Acquired.Should().BeTrue();

        await using var second = await locks.AcquireAsync(resource, TimeSpan.FromSeconds(30));
        second.Acquired.Should().BeFalse();

        await first.DisposeAsync();
        await using var third = await locks.AcquireAsync(resource, TimeSpan.FromSeconds(30));
        third.Acquired.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_check_is_healthy()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var check = new RedisHealthCheck(mux);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    private sealed class Sample
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
