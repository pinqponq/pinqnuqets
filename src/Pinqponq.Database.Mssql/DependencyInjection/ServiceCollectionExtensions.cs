using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pinqponq.Database.Mssql.DependencyInjection;

/// <summary>
/// Registration helpers for the MSSQL connection layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers an <see cref="ISqlConnectionFactory"/> with retry resiliency.</summary>
    public static IServiceCollection AddPinqponqMssql(
        this IServiceCollection services,
        Action<MssqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<ISqlConnectionFactory>(sp =>
            new SqlConnectionFactory(sp.GetRequiredService<IOptions<MssqlOptions>>().Value));

        return services;
    }

    /// <summary>Adds a SQL Server <c>SELECT 1</c> health check.</summary>
    public static IHealthChecksBuilder AddPinqponqMssql(
        this IHealthChecksBuilder builder,
        string name = "mssql",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<MssqlHealthCheck>(name, failureStatus, tags ?? []);
    }
}
