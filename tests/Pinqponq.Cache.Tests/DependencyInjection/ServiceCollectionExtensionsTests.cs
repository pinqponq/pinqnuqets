using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Cache.DependencyInjection;
using Xunit;

namespace Pinqponq.Cache.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqCache_registers_cache_and_lock()
    {
        var services = new ServiceCollection();
        services.AddPinqponqCache(o => o.ConnectionString = "localhost:6379");

        services.Should().Contain(d => d.ServiceType == typeof(ICacheService));
        services.Should().Contain(d => d.ServiceType == typeof(IDistributedLock));
    }

    [Fact]
    public void AddPinqponqRedis_registers_health_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqCache(o => o.ConnectionString = "localhost:6379,abortConnect=false");
        services.AddHealthChecks().AddPinqponqRedis();

        services.Should().Contain(d => d.ServiceType == typeof(HealthCheckService));
    }
}
