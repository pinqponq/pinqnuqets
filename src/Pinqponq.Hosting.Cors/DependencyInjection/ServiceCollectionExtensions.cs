using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Cors.DependencyInjection;

/// <summary>Registration helpers for the CORS policy.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CORS and a policy built from <see cref="PinqponqCorsOptions"/>. The options are validated
    /// on startup. Call <c>app.UseCors()</c> (or <c>app.UseCors(name)</c> when <see cref="PinqponqCorsOptions.PolicyName"/>
    /// is set) after routing and before authorization.
    /// </summary>
    public static IServiceCollection AddPinqponqCors(
        this IServiceCollection services,
        Action<PinqponqCorsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddCorsOptions(services).Configure(configure);
        return services;
    }

    /// <summary>
    /// Registers CORS like <see cref="AddPinqponqCors(IServiceCollection, Action{PinqponqCorsOptions})"/>,
    /// with the options bound from a configuration section, for example <c>configuration.GetSection("Cors")</c>.
    /// </summary>
    public static IServiceCollection AddPinqponqCors(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        AddCorsOptions(services).Bind(configurationSection);
        return services;
    }

    private static OptionsBuilder<PinqponqCorsOptions> AddCorsOptions(IServiceCollection services)
    {
        services.AddCors();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PinqponqCorsOptions>, PinqponqCorsOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<CorsOptions>, PinqponqCorsPolicySetup>());
        return services.AddOptions<PinqponqCorsOptions>().ValidateOnStart();
    }
}
