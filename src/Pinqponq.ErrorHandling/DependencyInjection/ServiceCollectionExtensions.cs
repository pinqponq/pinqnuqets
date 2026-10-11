using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pinqponq.ErrorHandling.DependencyInjection;

/// <summary>
/// Registration helpers for the global error handling middleware.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers and configures <see cref="ErrorHandlingOptions"/>.</summary>
    public static IServiceCollection AddPinqponqErrorHandling(
        this IServiceCollection services,
        Action<ErrorHandlingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<ErrorHandlingOptions>();
        }

        return services;
    }

    /// <summary>
    /// Turns on localized error responses: the response code is looked up in the message
    /// catalog and the message is returned in the language of the request.
    /// </summary>
    public static IServiceCollection AddPinqponqErrorLocalization(
        this IServiceCollection services,
        Action<ErrorLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<ErrorLocalizationOptions>(localizationOptions =>
        {
            localizationOptions.Enabled = true;
            configure?.Invoke(localizationOptions);
        });

        return services;
    }

    /// <summary>
    /// Binds <see cref="MaintenanceModeOptions"/> to <paramref name="maintenanceModeSection"/>
    /// (e.g. <c>configuration.GetSection("MaintenanceMode")</c>); the middleware reads it per request.
    /// </summary>
    public static IServiceCollection AddPinqponqMaintenanceMode(
        this IServiceCollection services,
        IConfiguration maintenanceModeSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(maintenanceModeSection);

        services.Configure<MaintenanceModeOptions>(maintenanceModeSection);
        return services;
    }

    /// <summary>Registers an <see cref="IExceptionResolver"/>; resolvers run in registration order.</summary>
    public static IServiceCollection AddPinqponqExceptionResolver<TExceptionResolver>(this IServiceCollection services)
        where TExceptionResolver : class, IExceptionResolver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionResolver, TExceptionResolver>());
        return services;
    }

    /// <summary>Adds the <see cref="ExceptionHandlingMiddleware"/> to the pipeline.</summary>
    public static IApplicationBuilder UsePinqponqErrorHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
