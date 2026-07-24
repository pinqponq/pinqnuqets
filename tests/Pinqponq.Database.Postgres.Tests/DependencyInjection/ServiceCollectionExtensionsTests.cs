using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Database.Postgres.DependencyInjection;
using Xunit;

namespace Pinqponq.Database.Postgres.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqPostgres_registers_factory()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgres(o => o.ConnectionString = "Host=localhost;Database=x;Username=u;Password=p");

        services.Should().Contain(d => d.ServiceType == typeof(IPostgresConnectionFactory));
    }

    [Fact]
    public void AddPinqponqPostgres_health_check_registers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqPostgres(o => o.ConnectionString = "Host=localhost;Database=x;Username=u;Password=p");
        services.AddHealthChecks().AddPinqponqPostgres();

        services.Should().Contain(d => d.ServiceType == typeof(HealthCheckService));
    }
}
