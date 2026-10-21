using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.ErrorHandling.DependencyInjection;

namespace Pinqponq.RateLimiting.DependencyInjection;

/// <summary>Registration helpers for Pinqponq rate limiting.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core rate limiting with the policies set in <paramref name="configure"/> and
    /// validates them on startup. Add <c>app.UseRateLimiter()</c> to the pipeline.
    /// </summary>
    public static IServiceCollection AddPinqponqRateLimiting(
        this IServiceCollection services,
        Action<RateLimitingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RateLimitingOptions>().Configure(configure);
        return AddRateLimitingServices(services);
    }

    /// <summary>
    /// Registers ASP.NET Core rate limiting with the policies bound from <paramref name="configurationSection"/>
    /// (<c>Policies</c>, <c>GlobalLimits</c>, <c>UserIdClaimType</c>) and validates them on startup.
    /// <paramref name="configure"/> runs after binding, for settings configuration cannot hold, such as
    /// <see cref="RateLimitPolicyOptions.PartitionKeyResolver"/>. Add <c>app.UseRateLimiter()</c> to the pipeline.
    /// </summary>
    public static IServiceCollection AddPinqponqRateLimiting(
        this IServiceCollection services,
        IConfiguration configurationSection,
        Action<RateLimitingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        var optionsBuilder = services.AddOptions<RateLimitingOptions>().Bind(configurationSection);
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        return AddRateLimitingServices(services);
    }

    private static IServiceCollection AddRateLimitingServices(IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>());

        services.AddPinqponqErrorHandling();
        services.TryAddSingleton<IRateLimitRejectionWriter, ErrorResponseRejectionWriter>();
        services.TryAddSingleton(serviceProvider => new RateLimitPartitionKeyResolver(
            serviceProvider.GetRequiredService<IOptions<RateLimitingOptions>>().Value.UserIdClaimType,
            serviceProvider.GetRequiredService<ILogger<RateLimitPartitionKeyResolver>>()));

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>, RateLimitPartitionKeyResolver>(
                (rateLimiterOptions, rateLimitingOptions, partitionKeyResolver) =>
                    RateLimiterSetup.Apply(rateLimiterOptions, rateLimitingOptions.Value, partitionKeyResolver));
        return services;
    }
}
