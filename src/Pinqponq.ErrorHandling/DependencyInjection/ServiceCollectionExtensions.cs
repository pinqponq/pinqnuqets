using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Adds the <see cref="ExceptionHandlingMiddleware"/> to the pipeline.</summary>
    public static IApplicationBuilder UsePinqponqErrorHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
