using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Database.Mssql.DependencyInjection;
using Xunit;

namespace Pinqponq.Database.Mssql.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqMssql_registers_factory()
    {
        var services = new ServiceCollection();
        services.AddPinqponqMssql(o => o.ConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;");

        services.Should().Contain(d => d.ServiceType == typeof(ISqlConnectionFactory));
    }

    [Fact]
    public void AddPinqponqMssql_health_check_registers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqMssql(o => o.ConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;");
        services.AddHealthChecks().AddPinqponqMssql();

        services.Should().Contain(d => d.ServiceType == typeof(HealthCheckService));
    }
}
