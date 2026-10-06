using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pinqponq.Identity.Jwt;

namespace Pinqponq.Identity.DependencyInjection;

/// <summary>
/// Registers ASP.NET Core JWT bearer authentication derived from <see cref="JwtOptions"/>.
/// </summary>
public static class JwtBearerServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="JwtOptions"/> and adds JWT bearer authentication, for a service that only validates tokens.
    /// </summary>
    public static AuthenticationBuilder AddPinqponqJwtBearer(
        this IServiceCollection services,
        Action<JwtOptions> configureJwt,
        Action<JwtBearerOptions>? configureBearer = null,
        string authenticationScheme = JwtBearerDefaults.AuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureJwt);

        services.AddOptions<JwtOptions>()
            .Configure(configureJwt)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>());
        services.TryAddSingleton(serviceProvider =>
            new JwtSigningKeyResolver(serviceProvider.GetRequiredService<IOptions<JwtOptions>>().Value));

        return services.AddPinqponqJwtBearer(configureBearer, authenticationScheme);
    }

    /// <summary>
    /// Adds JWT bearer authentication using the <see cref="JwtOptions"/> bound by <c>AddPinqponqIdentity</c>.
    /// </summary>
    public static AuthenticationBuilder AddPinqponqJwtBearer(
        this IServiceCollection services,
        Action<JwtBearerOptions>? configureBearer = null,
        string authenticationScheme = JwtBearerDefaults.AuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);

        var authenticationBuilder = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = authenticationScheme;
                options.DefaultChallengeScheme = authenticationScheme;
            })
            .AddJwtBearer(authenticationScheme, _ => { });

        services.AddOptions<JwtBearerOptions>(authenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKeyResolver>(
                (bearerOptions, jwtOptions, signingKeyResolver) =>
                {
                    var signingOptions = jwtOptions.Value;
                    var validationKey = signingKeyResolver.CreateValidationKey();
                    var isIssuerValidated = signingOptions.ValidateIssuer
                        && !string.IsNullOrEmpty(signingOptions.Issuer);
                    var isAudienceValidated = signingOptions.ValidateAudience
                        && !string.IsNullOrEmpty(signingOptions.Audience);

                    bearerOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = validationKey,
                        ValidateIssuer = isIssuerValidated,
                        ValidIssuer = signingOptions.Issuer,
                        ValidateAudience = isAudienceValidated,
                        ValidAudience = signingOptions.Audience,
                        ValidateLifetime = signingOptions.ValidateLifetime,
                        ClockSkew = signingOptions.ClockSkew
                    };
                });

        if (configureBearer is not null)
        {
            services.AddOptions<JwtBearerOptions>(authenticationScheme).Configure(configureBearer);
        }

        return authenticationBuilder;
    }
}
