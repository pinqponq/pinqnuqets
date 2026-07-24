using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Pinqponq.Database.Mongo.DependencyInjection;
using Xunit;

namespace Pinqponq.Database.Mongo.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqMongo_registers_client_and_database()
    {
        var services = new ServiceCollection();
        services.AddPinqponqMongo(o =>
        {
            o.ConnectionString = "mongodb://localhost:27017";
            o.DatabaseName = "testdb";
        });

        services.Should().Contain(d => d.ServiceType == typeof(IMongoClient));
        services.Should().Contain(d => d.ServiceType == typeof(IMongoDatabase));
    }

    [Fact]
    public void AddPinqponqMongo_health_check_registers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqMongo(o =>
        {
            o.ConnectionString = "mongodb://localhost:27017";
            o.DatabaseName = "testdb";
        });
        services.AddHealthChecks().AddPinqponqMongo();

        services.Should().Contain(d => d.ServiceType == typeof(HealthCheckService));
    }
}
