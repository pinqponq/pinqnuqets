using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pinqponq.ErrorHandling.DependencyInjection;

namespace Pinqponq.Hosting.Maintenance.DependencyInjection;

/// <summary>Registration helpers for maintenance mode.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaintenanceModeOptions"/> to the <c>MaintenanceMode</c> section of
    /// <paramref name="configuration"/> (then <paramref name="configure"/>) and validates it at startup.
    /// A reload of that section takes effect on the next request. Add <c>app.UsePinqponqMaintenanceMode()</c>
    /// to the pipeline.
    /// </summary>
    public static IServiceCollection AddPinqponqMaintenanceMode(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MaintenanceModeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var optionsBuilder = services.AddOptions<MaintenanceModeOptions>()
            .Bind(configuration.GetSection(MaintenanceModeOptions.SectionName))
            .ValidateOnStart();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MaintenanceModeOptions>, MaintenanceModeOptionsValidator>());
        services.AddPinqponqErrorHandling();
        return services;
    }

    /// <summary>
    /// Adds the maintenance mode middleware. Place it before routing, authentication and anything else that
    /// should not run during maintenance. Place it after <c>UsePinqponqRequestLogging</c> when the blocked
    /// requests should be logged as requests too.
    /// </summary>
    public static IApplicationBuilder UsePinqponqMaintenanceMode(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<MaintenanceModeMiddleware>();
    }
}
