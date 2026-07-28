using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Pinqponq.Database.Mongo.DependencyInjection;

/// <summary>
/// Registration helpers for the MongoDB connection layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared <see cref="IMongoClient"/> and the default
    /// <see cref="IMongoDatabase"/> resolved from configuration.
    /// </summary>
    public static IServiceCollection AddPinqponqMongo(
        this IServiceCollection services,
        Action<MongoOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MongoOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MongoOptions>, MongoOptionsValidator>());

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName);
        });

        return services;
    }

    /// <summary>Adds a MongoDB <c>ping</c> health check.</summary>
    public static IHealthChecksBuilder AddPinqponqMongo(
        this IHealthChecksBuilder builder,
        string name = "mongodb",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<MongoHealthCheck>(name, failureStatus, tags ?? []);
    }
}
