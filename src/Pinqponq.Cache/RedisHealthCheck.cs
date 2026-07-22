using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Pinqponq.Cache;

/// <summary>
/// Health check that PINGs Redis.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connection;

    /// <summary>Creates the health check over a shared connection multiplexer.</summary>
    public RedisHealthCheck(IConnectionMultiplexer connection) =>
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.GetDatabase().PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
