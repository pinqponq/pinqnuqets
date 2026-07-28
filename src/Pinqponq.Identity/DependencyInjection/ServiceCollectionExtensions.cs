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
    /// application must provide its own storage implementation. Applications that only
    /// want JWTs and password hashing may leave it unregistered; the missing store is
    /// then reported when <see cref="IRefreshTokenService"/> is first resolved.
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

        services.Configure(configureJwt);
        if (configureRefreshTokens is not null)
        {
            services.Configure(configureRefreshTokens);
        }

        // JWT: the resolver is derived from the bound options so signing and validation
        // stay consistent.
        services.TryAddSingleton(sp =>
            new JwtSigningKeyResolver(sp.GetRequiredService<IOptions<JwtOptions>>().Value));
        services.TryAddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.TryAddSingleton<IJwtTokenValidator, JwtTokenValidator>();

        // Passwords.
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // Refresh tokens (store provided by the consumer). Registered through a factory
        // rather than by implementation type: ASP.NET Core validates every type-registered
        // descriptor when it builds the container in Development, so registering the
        // implementation type would abort startup for every application that uses this
        // package for JWTs and password hashing but never issues a refresh token.
        services.TryAddScoped<IRefreshTokenService>(sp => new RefreshTokenService(
            sp.GetService<IRefreshTokenStore>() ?? throw new InvalidOperationException(
                $"{nameof(IRefreshTokenService)} needs an {nameof(IRefreshTokenStore)}, which "
                + "this package does not provide. Register the application's own storage, for "
                + $"example services.AddScoped<{nameof(IRefreshTokenStore)}, MyRefreshTokenStore>()."),
            sp.GetRequiredService<IOptions<RefreshTokenOptions>>()));

        return services;
    }
}
