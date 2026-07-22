using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace Pinqponq.Database.Mongo;

/// <summary>
/// Health check that issues a <c>ping</c> command against the configured database.
/// </summary>
public sealed class MongoHealthCheck : IHealthCheck
{
    private readonly IMongoDatabase _database;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the health check over the resolved database.</summary>
    public MongoHealthCheck(IMongoDatabase database, IOptions<MongoOptions> options)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = value.RetryCount,
                Delay = value.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<MongoConnectionException>()
                    .Handle<TimeoutException>(),
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(
                async token => await _database
                    .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: token)
                    .ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is unreachable.", ex);
        }
    }
}
