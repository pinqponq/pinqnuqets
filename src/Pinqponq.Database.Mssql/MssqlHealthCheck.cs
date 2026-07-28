using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pinqponq.Database.Mssql;

/// <summary>
/// Health check that opens a SQL Server connection once and runs <c>SELECT 1</c>
/// without the application retry pipeline.
/// </summary>
public sealed class MssqlHealthCheck : IHealthCheck
{
    private readonly MssqlOptions _options;

    /// <summary>Creates the health check over configured options.</summary>
    public MssqlHealthCheck(IOptions<MssqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
