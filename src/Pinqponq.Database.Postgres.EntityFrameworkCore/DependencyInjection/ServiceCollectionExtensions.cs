using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Pinqponq.Database.Postgres.EntityFrameworkCore.DependencyInjection;

/// <summary>
/// Registration helpers for an EF Core context on PostgreSQL.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> on PostgreSQL. The context's connections come
    /// from one <see cref="NpgsqlDataSource"/> built at first use from
    /// <paramref name="connectionString"/> and <paramref name="configureDataSource"/>
    /// (e.g. <c>dataSource =&gt; dataSource.EnableDynamicJson(...)</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    public static IServiceCollection AddPinqponqPostgresDbContext<TDbContext>(
        this IServiceCollection services,
        string? connectionString,
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"PostgreSQL connection string for {typeof(TDbContext).Name} is missing.");
        }

        services.TryAddSingleton(_ =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            configureDataSource?.Invoke(dataSourceBuilder);
            return new PostgresDbContextDataSource<TDbContext>(dataSourceBuilder.Build());
        });

        services.AddDbContext<TDbContext>((serviceProvider, contextOptions) =>
        {
            var dataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<TDbContext>>().DataSource;
            contextOptions.UseNpgsql(dataSource, configureNpgsql);
        });

        return services;
    }
}
