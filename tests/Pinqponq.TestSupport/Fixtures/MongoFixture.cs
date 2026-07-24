using Testcontainers.MongoDb;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>Shared MongoDB container for integration tests.</summary>
public class MongoFixture 
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7.0").Build();

    /// <summary>MongoDB connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
