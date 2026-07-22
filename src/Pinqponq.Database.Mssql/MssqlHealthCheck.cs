using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pinqponq.Database.Mssql;

/// <summary>
/// Health check that opens a SQL Server connection and runs <c>SELECT 1</c>.
/// </summary>
public sealed class MssqlHealthCheck : IHealthCheck
{
    private readonly ISqlConnectionFactory _connectionFactory;

    /// <summary>Creates the health check over the connection factory.</summary>
    public MssqlHealthCheck(ISqlConnectionFactory connectionFactory) =>
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
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server is unreachable.", ex);
        }
    }
}
