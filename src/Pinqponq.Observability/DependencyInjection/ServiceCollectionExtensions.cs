using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pinqloq;

namespace Pinqponq.Observability.DependencyInjection;

/// <summary>
/// Registration helpers for Pinqloq observability.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string PinqloqSecretKeyConfigurationKey = "Pinqloq:SecretKey";

    /// <summary>
    /// Binds <see cref="ObservabilityOptions"/> to the <c>Observability</c> section (then
    /// <paramref name="configure"/>), validates it at startup, registers the Pinqloq client
    /// (secret from <c>Pinqloq:SecretKey</c>, job records to
    /// <see cref="ObservabilityOptions.JobsCollectionName"/>, machine name as the device
    /// identifier) and <see cref="ILogService"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var observabilitySection = configuration.GetSection(ObservabilityOptions.SectionName);
        var optionsBuilder = services.AddOptions<ObservabilityOptions>()
            .Bind(observabilitySection)
            .ValidateOnStart();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ObservabilityOptions>, ObservabilityOptionsValidator>());

        var registrationOptions = new ObservabilityOptions();
        observabilitySection.Bind(registrationOptions);
        configure?.Invoke(registrationOptions);

        services.AddPinqloq(pinqloqOptions =>
        {
            pinqloqOptions.SecretKey = configuration[PinqloqSecretKeyConfigurationKey] ?? string.Empty;
            pinqloqOptions.ApiLogsCollectionName = registrationOptions.JobsCollectionName;
            pinqloqOptions.DeviceIdentifier = Environment.MachineName;
        });

        services.TryAddSingleton<ILogService, LogService>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="RequestLoggingMiddleware"/>. Register it before
    /// <c>UsePinqponqErrorHandling</c>, so it sees the error response and its detail.
    /// </summary>
    public static IApplicationBuilder UsePinqponqRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
