using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Database.Mssql.DependencyInjection;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Mssql.Tests;

[Collection(MsSqlCollection.Name)]
public sealed class SqlConnectionFactoryTests
{
    private readonly MsSqlCollectionFixture _fixture;

    public SqlConnectionFactoryTests(MsSqlCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenConnectionAsync_succeeds()
    {
        var services = new ServiceCollection();
        services.AddPinqponqMssql(o => o.ConnectionString = _fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ISqlConnectionFactory>();

        await using var conn = await factory.OpenConnectionAsync();
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_check_is_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqMssql(o => o.ConnectionString = _fixture.ConnectionString);
        services.AddHealthChecks().AddPinqponqMssql();
        await using var sp = services.BuildServiceProvider();

        var report = await sp.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, report.Status);
    }
}
