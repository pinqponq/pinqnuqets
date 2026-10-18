using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat.Mail.DependencyInjection;

/// <summary>Registration helpers for the RevenueCat subscription mails.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RevenueCatMailListener"/> as an <see cref="IRevenueCatEventListener"/>,
    /// the default <see cref="IRevenueCatMailRenderer"/> and the product's <typeparamref name="TResolver"/>,
    /// and validates the options on startup. Needs <c>AddPinqponqRevenueCat</c> (which calls the
    /// listener) and <c>AddPinqponqMail</c> (which sends the mail).
    /// </summary>
    /// <typeparam name="TResolver">The product's implementation of <see cref="IRevenueCatMailRecipientResolver"/>, registered scoped.</typeparam>
    public static IServiceCollection AddPinqponqRevenueCatMail<TResolver>(
        this IServiceCollection services,
        Action<RevenueCatMailOptions> configure)
        where TResolver : class, IRevenueCatMailRecipientResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RevenueCatMailOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RevenueCatMailOptions>, RevenueCatMailOptionsValidator>());

        services.TryAddScoped<IRevenueCatMailRecipientResolver, TResolver>();
        services.TryAddSingleton<IRevenueCatMailRenderer, RevenueCatMailRenderer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevenueCatEventListener, RevenueCatMailListener>());
        return services;
    }
}
