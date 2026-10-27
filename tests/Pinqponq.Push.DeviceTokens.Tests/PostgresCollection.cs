using Microsoft.EntityFrameworkCore;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Push.DeviceTokens.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<DeviceTokenDatabaseFixture>
{
    public const string Name = "postgres";
}

public sealed class DeviceTokenDatabaseFixture : PostgresFixture, IAsyncLifetime
{
    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(ConnectionString).Options);
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyPinqponqDeviceTokens();
}
