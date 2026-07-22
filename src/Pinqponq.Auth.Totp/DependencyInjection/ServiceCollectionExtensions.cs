using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pinqponq.Auth.Totp.DependencyInjection;

/// <summary>
/// Registration helpers for TOTP 2FA.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ITotpService"/> with optional configuration.</summary>
    public static IServiceCollection AddPinqponqTotp(
        this IServiceCollection services,
        Action<TotpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ITotpService, TotpService>();
        return services;
    }
}
