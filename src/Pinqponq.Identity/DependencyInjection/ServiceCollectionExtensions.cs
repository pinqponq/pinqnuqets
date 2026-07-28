using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Passwords;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the Pinqponq.Identity primitives.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the JWT generator/validator, the PBKDF2 password hasher and the refresh
    /// token service.
    /// </summary>
    /// <remarks>
    /// <see cref="IRefreshTokenStore"/> is intentionally not registered — the consuming
    /// application must provide its own storage implementation.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configureJwt">Configures <see cref="JwtOptions"/>.</param>
    /// <param name="configureRefreshTokens">Optionally configures <see cref="RefreshTokenOptions"/>.</param>
    public static IServiceCollection AddPinqponqIdentity(
        this IServiceCollection services,
        Action<JwtOptions> configureJwt,
        Action<RefreshTokenOptions>? configureRefreshTokens = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureJwt);

        services.AddOptions<JwtOptions>()
            .Configure(configureJwt)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>());

        var refresh = services.AddOptions<RefreshTokenOptions>();
        if (configureRefreshTokens is not null)
        {
            refresh.Configure(configureRefreshTokens);
        }

        refresh.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RefreshTokenOptions>, RefreshTokenOptionsValidator>());

        // JWT: the resolver is derived from the bound options so signing and validation
        // stay consistent.
        services.TryAddSingleton(sp =>
            new JwtSigningKeyResolver(sp.GetRequiredService<IOptions<JwtOptions>>().Value));
        services.TryAddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        // Scoped so optional IAccessTokenRevocationStore can be Scoped without captive dependency.
        services.TryAddScoped<IJwtTokenValidator, JwtTokenValidator>();

        // Requires a consumer-registered IAccessTokenRevocationStore when resolved.
        services.TryAddScoped<IAccessTokenRevocationService, AccessTokenRevocationService>();

        // Passwords.
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // Refresh tokens (store provided by the consumer).
        services.TryAddScoped<IRefreshTokenService, RefreshTokenService>();

        return services;
    }
}
