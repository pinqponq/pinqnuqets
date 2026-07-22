using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace Pinqponq.Database.Mssql;

/// <summary>
/// Default <see cref="ISqlConnectionFactory"/> that opens connections through a
/// Polly retry pipeline covering transient SQL Server faults.
/// </summary>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    // Well-known transient SQL Server error numbers (timeouts, throttling, failover).
    private static readonly HashSet<int> TransientErrorNumbers =
        [-2, 20, 64, 233, 10053, 10054, 10060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920, 4060, 4221];

    private readonly string _connectionString;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the factory with the configured connection string and retry policy.</summary>
    public SqlConnectionFactory(MssqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.ConnectionString;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.RetryCount,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>(static e => IsTransient(e))
                    .Handle<TimeoutException>(),
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await _pipeline.ExecuteAsync(
            async token =>
            {
                var connection = new SqlConnection(_connectionString);
                try
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    return connection;
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);

    private static bool IsTransient(SqlException exception)
    {
        foreach (SqlError error in exception.Errors)
        {
            if (TransientErrorNumbers.Contains(error.Number))
            {
                return true;
            }
        }

        return false;
    }
}
