using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pinqponq.Auth.Sso.Abstractions;

namespace Pinqponq.Auth.Sso.Google.DependencyInjection;

/// <summary>
/// Registration helpers for Google SSO.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GoogleAuthProvider"/> as an <see cref="IExternalAuthProvider"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqGoogleSso(
        this IServiceCollection services,
        Action<GoogleAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IExternalAuthProvider, GoogleAuthProvider>();
        return services;
    }
}
