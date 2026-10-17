using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.ApiKey.DependencyInjection;

/// <summary>Registration helpers for <see cref="RequireApiKeyAttribute"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the key settings that <c>[RequireApiKey(name)]</c> checks and validates them on
    /// startup. Call it once per name to guard endpoint groups with different keys.
    /// </summary>
    public static IServiceCollection AddPinqponqApiKey(
        this IServiceCollection services,
        string name,
        Action<ApiKeyOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddOptions<ApiKeyOptions>(name)
            .Configure(configure)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiKeyOptions>>(new ApiKeyOptionsValidator(name));
        return services;
    }
}
