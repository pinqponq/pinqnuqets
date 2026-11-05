using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests;

/// <summary>
/// One empty PostgreSQL database per test, created from <see cref="TestRefreshTokenDbContext"/>'s
/// model. Every context it hands out is disposed with it.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly DbContextOptions<TestRefreshTokenDbContext> _contextOptions;
    private readonly List<TestRefreshTokenDbContext> _contexts = [];
    private readonly object _contextsGate = new();

    private TestDatabase(string connectionString)
    {
        _contextOptions = new DbContextOptionsBuilder<TestRefreshTokenDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    public static async Task<TestDatabase> CreateAsync(PostgresCollectionFixture fixture)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = $"refresh_tokens_{Guid.NewGuid():N}"
        };
        var database = new TestDatabase(connectionStringBuilder.ConnectionString);
        await database.CreateContext().Database.EnsureCreatedAsync();
        return database;
    }

    public TestRefreshTokenDbContext CreateContext()
    {
        var context = new TestRefreshTokenDbContext(_contextOptions);
        lock (_contextsGate)
        {
            _contexts.Add(context);
        }

        return context;
    }

    public EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, TRecord> CreateStore<TRecord>(
        EntityFrameworkRefreshTokenStoreOptions<TRecord> storeOptions,
        TimeProvider timeProvider)
        where TRecord : class, IRefreshTokenRecord, new() =>
        new(CreateContext(), Options.Create(storeOptions), timeProvider);

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }
    }
}
