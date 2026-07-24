using Testcontainers.MsSql;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>Shared SQL Server container for integration tests.</summary>
public class MsSqlFixture 
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    /// <summary>SQL Server connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
