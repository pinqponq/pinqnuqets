using Microsoft.Extensions.DependencyInjection;
using Pinqponq.ErrorHandling.DependencyInjection;

namespace Pinqponq.ErrorHandling.Postgres.DependencyInjection;

/// <summary>
/// Registration helpers for the PostgreSQL error mapping.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresExceptionResolver"/> and adds the <c>database_*</c> messages to the error localization catalog.
    /// </summary>
    public static IServiceCollection AddPinqponqPostgresErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPinqponqExceptionResolver<PostgresExceptionResolver>();
        services.Configure<ErrorLocalizationOptions>(localizationOptions =>
        {
            var catalogAssembly = typeof(PostgresExceptionResolver).Assembly;
            if (!localizationOptions.CatalogAssemblies.Contains(catalogAssembly))
            {
                localizationOptions.CatalogAssemblies.Add(catalogAssembly);
            }
        });

        return services;
    }
}
