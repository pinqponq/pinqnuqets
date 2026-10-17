using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat.DependencyInjection;

/// <summary>Registration helpers for RevenueCat webhook handling.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRevenueCatEventHandler"/>, <see cref="IRevenueCatWebhookAuthenticator"/>
    /// and the product's <typeparamref name="TStore"/>.
    /// </summary>
    /// <typeparam name="TStore">The product's implementation of <see cref="IRevenueCatSubscriptionStore"/>, registered scoped.</typeparam>
    public static IServiceCollection AddPinqponqRevenueCat<TStore>(
        this IServiceCollection services,
        Action<RevenueCatOptions> configure)
        where TStore : class, IRevenueCatSubscriptionStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RevenueCatOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RevenueCatOptions>, RevenueCatOptionsValidator>());

        services.TryAddSingleton<IRevenueCatWebhookAuthenticator, RevenueCatWebhookAuthenticator>();
        services.TryAddSingleton<IRevenueCatWebhookObserver, NullRevenueCatWebhookObserver>();
        services.TryAddScoped<IRevenueCatSubscriptionStore, TStore>();
        services.TryAddScoped<IRevenueCatEventHandler, RevenueCatEventHandler>();
        return services;
    }
}
