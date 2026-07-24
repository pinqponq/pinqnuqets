using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Pinqponq.Database.Mongo.DependencyInjection;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Mongo.Tests;

[Collection(MongoCollection.Name)]
public sealed class MongoHealthCheckTests
{
    private readonly MongoCollectionFixture _fixture;

    public MongoHealthCheckTests(MongoCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Database_ping_succeeds()
    {
        var services = new ServiceCollection();
        services.AddPinqponqMongo(o =>
        {
            o.ConnectionString = _fixture.ConnectionString;
            o.DatabaseName = "pinq_tests";
        });
        await using var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IMongoDatabase>();

        var result = await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        result["ok"].ToDouble().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_check_is_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqMongo(o =>
        {
            o.ConnectionString = _fixture.ConnectionString;
            o.DatabaseName = "pinq_tests";
        });
        services.AddHealthChecks().AddPinqponqMongo();
        await using var sp = services.BuildServiceProvider();

        var report = await sp.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Status.Should().Be(HealthStatus.Healthy);
    }
}
