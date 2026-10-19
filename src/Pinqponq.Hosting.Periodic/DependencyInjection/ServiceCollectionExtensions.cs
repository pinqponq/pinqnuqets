using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Periodic.DependencyInjection;

/// <summary>Registration helpers for periodic jobs.</summary>
public static class ServiceCollectionExtensions
{
    private const int StaleIntervalCount = 2;

    /// <summary>
    /// Registers <typeparamref name="TJob"/> as a scoped service and a hosted service that runs it
    /// on the configured schedule. The options are validated on startup.
    /// </summary>
    public static IServiceCollection AddPinqponqPeriodicJob<TJob>(
        this IServiceCollection services,
        Action<PeriodicJobOptions> configure)
        where TJob : class, IPeriodicJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddPeriodicJobOptions<TJob>(services).Configure(configure);
        AddPeriodicJobServices<TJob>(services);
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TJob"/> like
    /// <see cref="AddPinqponqPeriodicJob{TJob}(IServiceCollection, Action{PeriodicJobOptions})"/>,
    /// with the options bound from a configuration section.
    /// </summary>
    public static IServiceCollection AddPinqponqPeriodicJob<TJob>(
        this IServiceCollection services,
        IConfiguration configurationSection)
        where TJob : class, IPeriodicJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        AddPeriodicJobOptions<TJob>(services).Bind(configurationSection);
        AddPeriodicJobServices<TJob>(services);
        return services;
    }

    /// <summary>
    /// Adds a health check that is unhealthy when <typeparamref name="TJob"/> has not finished a
    /// run successfully for longer than <paramref name="staleAfter"/>. A disabled job is healthy.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Default: the job's type name.</param>
    /// <param name="staleAfter">How long the job may go without a successful run. Default: two intervals.</param>
    /// <param name="failureStatus">The status reported when the job is late.</param>
    /// <param name="tags">Tags for filtering health checks.</param>
    public static IHealthChecksBuilder AddPinqponqPeriodicJob<TJob>(
        this IHealthChecksBuilder builder,
        string? name = null,
        TimeSpan? staleAfter = null,
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
        where TJob : class, IPeriodicJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "The stale time must be greater than zero.");
        }

        builder.Services.TryAddSingleton<PeriodicJobRunRegistry>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        var jobKey = PeriodicJobKey.For<TJob>();
        var healthCheckName = name ?? typeof(TJob).Name;
        var registration = new HealthCheckRegistration(
            healthCheckName,
            serviceProvider => CreateHealthCheck<TJob>(serviceProvider, jobKey, staleAfter),
            failureStatus,
            tags);
        return builder.Add(registration);
    }

    private static OptionsBuilder<PeriodicJobOptions> AddPeriodicJobOptions<TJob>(IServiceCollection services)
        where TJob : class, IPeriodicJob
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PeriodicJobOptions>, PeriodicJobOptionsValidator>());
        return services.AddOptions<PeriodicJobOptions>(PeriodicJobKey.For<TJob>())
            .ValidateOnStart();
    }

    private static void AddPeriodicJobServices<TJob>(IServiceCollection services)
        where TJob : class, IPeriodicJob
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PeriodicJobRunRegistry>();
        services.TryAddScoped<TJob>();
        services.AddHostedService<PeriodicJobService<TJob>>();
    }

    private static PeriodicJobHealthCheck CreateHealthCheck<TJob>(
        IServiceProvider serviceProvider,
        string jobKey,
        TimeSpan? staleAfter)
        where TJob : class, IPeriodicJob
    {
        var jobOptions = serviceProvider.GetRequiredService<IOptionsMonitor<PeriodicJobOptions>>().Get(jobKey);
        var jobName = PeriodicJobKey.ResolveName<TJob>(jobOptions);
        var staleTime = staleAfter ?? jobOptions.Interval * StaleIntervalCount;
        return new PeriodicJobHealthCheck(
            jobKey,
            jobName,
            jobOptions,
            staleTime,
            serviceProvider.GetRequiredService<PeriodicJobRunRegistry>(),
            serviceProvider.GetRequiredService<TimeProvider>());
    }
}
