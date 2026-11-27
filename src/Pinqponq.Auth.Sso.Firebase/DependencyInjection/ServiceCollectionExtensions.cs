using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Sso.Firebase.DependencyInjection;

/// <summary>
/// Registration helpers for Firebase SSO.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FirebaseAuthProvider"/> and the named <see cref="HttpClient"/> that downloads
    /// the public keys.
    /// </summary>
    /// <remarks>
    /// The provider is registered as <see cref="FirebaseAuthProvider"/> only, not as
    /// <c>IExternalAuthProvider</c>, so it does not replace or hide another provider registered under
    /// that interface.
    /// </remarks>
    public static IServiceCollection AddPinqponqFirebaseSso(
        this IServiceCollection services,
        Action<FirebaseAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<FirebaseAuthOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<FirebaseAuthOptions>, FirebaseAuthOptionsValidator>());

        services.AddHttpClient(FirebaseAuthProvider.HttpClientName);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FirebaseSigningKeyCache>();
        services.TryAddSingleton(serviceProvider => new FirebaseAuthProvider(
            serviceProvider.GetRequiredService<IOptions<FirebaseAuthOptions>>(),
            serviceProvider.GetRequiredService<FirebaseSigningKeyCache>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<FirebaseAuthProvider>>()));
        return services;
    }
}
