using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Pinqponq.Database.Postgres;

/// <summary>
/// Health check that opens a connection from the shared <see cref="NpgsqlDataSource"/>
/// and runs <c>SELECT 1</c> without the application retry pipeline.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Creates the health check over the shared data source.</summary>
    public PostgresHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres is unreachable.", ex);
        }
    }
}
