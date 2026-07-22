using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Pinqponq.Database.Postgres.DependencyInjection;

/// <summary>
/// Registration helpers for the Postgres connection layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared <see cref="NpgsqlDataSource"/> and an
    /// <see cref="IPostgresConnectionFactory"/> with retry resiliency.
    /// </summary>
    public static IServiceCollection AddPinqponqPostgres(
        this IServiceCollection services,
        Action<PostgresOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        services.TryAddSingleton<IPostgresConnectionFactory>(sp =>
            new PostgresConnectionFactory(
                sp.GetRequiredService<NpgsqlDataSource>(),
                sp.GetRequiredService<IOptions<PostgresOptions>>().Value));

        return services;
    }

    /// <summary>Adds a Postgres <c>SELECT 1</c> health check.</summary>
    public static IHealthChecksBuilder AddPinqponqPostgres(
        this IHealthChecksBuilder builder,
        string name = "postgres",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<PostgresHealthCheck>(name, failureStatus, tags ?? []);
    }
}
