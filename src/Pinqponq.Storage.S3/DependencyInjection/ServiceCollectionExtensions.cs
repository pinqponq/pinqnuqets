using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3.DependencyInjection;

/// <summary>
/// Registration helpers for S3 object storage.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IObjectStorage"/> and a shared <see cref="IAmazonS3"/> client with an inline configuration action.</summary>
    public static IServiceCollection AddPinqponqObjectStorage(
        this IServiceCollection services,
        Action<ObjectStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ObjectStorageOptions>()
            .Configure(configure)
            .ValidateOnStart();
        return AddObjectStorageServices(services);
    }

    /// <summary>Registers <see cref="IObjectStorage"/> and a shared <see cref="IAmazonS3"/> client bound to a configuration section.</summary>
    public static IServiceCollection AddPinqponqObjectStorage(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        if (!configurationSection.Exists())
        {
            throw new InvalidOperationException(
                $"Configuration section '{configurationSection.Path}' not found. Add the object storage configuration to appsettings.");
        }

        services.AddOptions<ObjectStorageOptions>()
            .Bind(configurationSection)
            .ValidateOnStart();
        return AddObjectStorageServices(services);
    }

    /// <summary>Adds a health check that sends a HEAD request for the configured bucket.</summary>
    public static IHealthChecksBuilder AddPinqponqObjectStorageHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "object-storage",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<ObjectStorageHealthCheck>(name, failureStatus, tags ?? []);
    }

    private static IServiceCollection AddObjectStorageServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ObjectStorageOptions>, ObjectStorageOptionsValidator>());
        services.TryAddSingleton(serviceProvider =>
            ObjectStorageClientFactory.CreateClient(serviceProvider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value));
        services.TryAddSingleton<IObjectStorage, S3ObjectStorage>();
        return services;
    }
}
