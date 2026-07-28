using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pinqponq.Cache.DependencyInjection;

/// <summary>
/// Registration helpers for the Redis cache.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared <see cref="IConnectionMultiplexer"/>, <see cref="ICacheService"/>
    /// and <see cref="IDistributedLock"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqCache(
        this IServiceCollection services,
        Action<RedisOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RedisOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>());

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var configuration = ConfigurationOptions.Parse(options.ConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.ConnectAsync(configuration)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        });

        services.TryAddSingleton<ICacheService, RedisCacheService>();
        services.TryAddSingleton<IDistributedLock, RedisDistributedLock>();

        return services;
    }

    /// <summary>Adds a Redis PING health check.</summary>
    public static IHealthChecksBuilder AddPinqponqRedis(
        this IHealthChecksBuilder builder,
        string name = "redis",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<RedisHealthCheck>(name, failureStatus, tags ?? []);
    }
}
