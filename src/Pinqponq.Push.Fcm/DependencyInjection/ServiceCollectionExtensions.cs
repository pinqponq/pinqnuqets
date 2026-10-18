using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Push.Fcm.DependencyInjection;

/// <summary>Registration helpers for the FCM sender.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IFcmSender"/> with an inline configuration action.</summary>
    public static IServiceCollection AddPinqponqFcm(
        this IServiceCollection services,
        Action<FcmOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<FcmOptions>()
            .Configure(configure)
            .ValidateOnStart();
        return AddFcmServices(services);
    }

    /// <summary>Registers <see cref="IFcmSender"/> bound to a configuration section.</summary>
    public static IServiceCollection AddPinqponqFcm(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        if (!configurationSection.Exists())
        {
            throw new InvalidOperationException(
                $"Configuration section '{configurationSection.Path}' not found. Add the FCM configuration to appsettings.");
        }

        services.AddOptions<FcmOptions>()
            .Bind(configurationSection)
            .ValidateOnStart();
        return AddFcmServices(services);
    }

    private static IServiceCollection AddFcmServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<FcmOptions>, FcmOptionsValidator>());
        services.TryAddSingleton<FirebaseAppProvider>();
        services.TryAddScoped<IFcmInvalidTokenHandler, NullFcmInvalidTokenHandler>();
        services.TryAddScoped<IFcmSender, FcmSender>();
        return services;
    }
}
