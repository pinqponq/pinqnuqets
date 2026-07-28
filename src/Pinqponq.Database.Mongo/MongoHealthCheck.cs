using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Pinqponq.Database.Mongo;

/// <summary>
/// Health check that issues a single <c>ping</c> command against the configured database.
/// </summary>
public sealed class MongoHealthCheck : IHealthCheck
{
    private readonly IMongoDatabase _database;

    /// <summary>Creates the health check over the resolved database.</summary>
    public MongoHealthCheck(IMongoDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _database
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is unreachable.", ex);
        }
    }
}
