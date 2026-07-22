using Npgsql;
using Polly;
using Polly.Retry;

namespace Pinqponq.Database.Postgres;

/// <summary>
/// Default <see cref="IPostgresConnectionFactory"/> backed by a shared
/// <see cref="NpgsqlDataSource"/> and a Polly retry pipeline.
/// </summary>
public sealed class PostgresConnectionFactory : IPostgresConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the factory over a data source with the configured retry policy.</summary>
    public PostgresConnectionFactory(NpgsqlDataSource dataSource, PostgresOptions options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        ArgumentNullException.ThrowIfNull(options);

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.RetryCount,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>(static e => e.IsTransient)
                    .Handle<TimeoutException>(),
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await _pipeline.ExecuteAsync(
            async token => await _dataSource.OpenConnectionAsync(token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
}
