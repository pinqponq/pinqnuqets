using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.ErrorHandling.DependencyInjection;

/// <summary>
/// Registration helpers for the global error handling middleware.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers and configures <see cref="ErrorHandlingOptions"/> and registers <see cref="IErrorResponseWriter"/>.</summary>
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

        services.TryAddSingleton<IErrorResponseWriter, LocalizedErrorResponseWriter>();
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

    /// <summary>Registers an <see cref="IExceptionResolver"/>; resolvers run in registration order.</summary>
    public static IServiceCollection AddPinqponqExceptionResolver<TExceptionResolver>(this IServiceCollection services)
        where TExceptionResolver : class, IExceptionResolver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionResolver, TExceptionResolver>());
        return services;
    }

    /// <summary>
    /// Answers exceptions whose message is the response code: a mapped exception type gets its status and its message as
    /// the response code. Runs as an <see cref="IExceptionResolver"/>, so a resolver registered earlier wins. The options
    /// are validated on startup.
    /// </summary>
    public static IServiceCollection AddPinqponqResponseCodeExceptions(
        this IServiceCollection services,
        Action<ResponseCodeExceptionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ResponseCodeExceptionOptions>().Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ResponseCodeExceptionOptions>, ResponseCodeExceptionOptionsValidator>());
        services.AddPinqponqExceptionResolver<ResponseCodeExceptionResolver>();
        return services;
    }

    /// <summary>Adds the <see cref="ExceptionHandlingMiddleware"/> to the pipeline.</summary>
    public static IApplicationBuilder UsePinqponqErrorHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
