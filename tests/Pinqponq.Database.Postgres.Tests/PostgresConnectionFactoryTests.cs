using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Database.Postgres.DependencyInjection;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresConnectionFactoryTests
{
    private readonly PostgresCollectionFixture _fixture;

    public PostgresConnectionFactoryTests(PostgresCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenConnectionAsync_succeeds()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgres(o => o.ConnectionString = _fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IPostgresConnectionFactory>();

        await using var conn = await factory.OpenConnectionAsync();
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_check_is_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqPostgres(o => o.ConnectionString = _fixture.ConnectionString);
        services.AddHealthChecks().AddPinqponqPostgres();
        await using var sp = services.BuildServiceProvider();

        var report = await sp.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Status.Should().Be(HealthStatus.Healthy);
    }
}
