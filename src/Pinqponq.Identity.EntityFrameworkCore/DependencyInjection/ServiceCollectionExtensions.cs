using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.EntityFrameworkCore.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EntityFrameworkRefreshTokenStore{TDbContext, TRecord}"/> as the scoped
    /// <see cref="IRefreshTokenStore"/>. <typeparamref name="TDbContext"/> must be registered by the
    /// application, and <typeparamref name="TRecord"/> must be part of its model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally configures <see cref="EntityFrameworkRefreshTokenStoreOptions{TRecord}"/>.</param>
    public static IServiceCollection AddPinqponqEntityFrameworkRefreshTokenStore<TDbContext, TRecord>(
        this IServiceCollection services,
        Action<EntityFrameworkRefreshTokenStoreOptions<TRecord>>? configure = null)
        where TDbContext : DbContext
        where TRecord : class, IRefreshTokenRecord, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var storeOptions = services.AddOptions<EntityFrameworkRefreshTokenStoreOptions<TRecord>>();
        if (configure is not null)
        {
            storeOptions.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRefreshTokenStore, EntityFrameworkRefreshTokenStore<TDbContext, TRecord>>();

        return services;
    }
}
