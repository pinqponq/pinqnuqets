using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Totp.DependencyInjection;

/// <summary>
/// Registration helpers for TOTP 2FA.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITotpService"/> with optional configuration. The consumer must
    /// also register <see cref="ITotpReplayStore"/> for <c>ValidateAsync</c> replay protection.
    /// </summary>
    public static IServiceCollection AddPinqponqTotp(
        this IServiceCollection services,
        Action<TotpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<TotpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TotpOptions>, TotpOptionsValidator>());

        // Scoped so consumer ITotpReplayStore can be Scoped without captive dependency.
        services.TryAddScoped<ITotpService, TotpService>();
        return services;
    }
}