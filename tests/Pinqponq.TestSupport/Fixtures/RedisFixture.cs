using Testcontainers.Redis;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>Shared Redis container for integration tests.</summary>
public class RedisFixture 
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();

    /// <summary>StackExchange.Redis-compatible connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
