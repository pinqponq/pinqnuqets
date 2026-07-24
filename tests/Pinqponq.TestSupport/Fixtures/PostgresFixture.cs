using Testcontainers.PostgreSql;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>Shared PostgreSQL container for integration tests.</summary>
public class PostgresFixture 
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>Npgsql connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
