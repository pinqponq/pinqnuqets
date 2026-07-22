using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Pinqponq.Database.Postgres;

/// <summary>
/// Health check that opens a Postgres connection and runs <c>SELECT 1</c>.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IPostgresConnectionFactory _connectionFactory;

    /// <summary>Creates the health check over the connection factory.</summary>
    public PostgresHealthCheck(IPostgresConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
