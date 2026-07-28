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

        // Unconditionally: without a configure action nothing else would register the
        // options services, and the defaults this package ships would be unresolvable.
        var options = services.AddOptions<TotpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TotpOptions>, TotpOptionsValidator>());

        // A factory rather than the implementation type: the ITotpReplayStore belongs to the
        // consuming application and is only needed by ValidateAsync, so a type registration
        // would abort container validation in Development for the sync/crypto-only use. Scoped
        // so the consumer's ITotpReplayStore can be Scoped without a captive dependency.
        services.TryAddScoped<ITotpService>(sp => new TotpService(
            sp.GetRequiredService<IOptions<TotpOptions>>(),
            sp.GetService<ITotpReplayStore>()));
        return services;
    }
}