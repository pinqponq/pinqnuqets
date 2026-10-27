using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pinqponq.Push.Fcm;

namespace Pinqponq.Push.DeviceTokens.DependencyInjection;

/// <summary>Registration helpers for the device token store.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDeviceTokenRegistry"/> over <typeparamref name="TContext"/>, whose model calls
    /// <see cref="ModelBuilderExtensions.ApplyPinqponqDeviceTokens"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqDeviceTokens<TContext>(
        this IServiceCollection services,
        Action<DeviceTokenOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DeviceTokenOptions();
        configure?.Invoke(options);
        if (options.MaxTokenLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DeviceTokenOptions.MaxTokenLength), "MaxTokenLength must be at least 1.");
        }

        if (options.MaxDeviceIdentifierLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DeviceTokenOptions.MaxDeviceIdentifierLength), "MaxDeviceIdentifierLength must be at least 1.");
        }

        if (options.Platforms.Count == 0 || options.Platforms.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Platforms must list at least one non-empty name.", nameof(DeviceTokenOptions.Platforms));
        }

        services.AddOptions<DeviceTokenOptions>().Configure(configure ?? (_ => { }));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IDeviceTokenRegistry, DeviceTokenRegistry<TContext>>();

        if (options.RemoveTokensFcmReportsInvalid)
        {
            services.Replace(ServiceDescriptor.Scoped<IFcmInvalidTokenHandler, FcmInvalidTokenRemover>());
        }

        return services;
    }
}
