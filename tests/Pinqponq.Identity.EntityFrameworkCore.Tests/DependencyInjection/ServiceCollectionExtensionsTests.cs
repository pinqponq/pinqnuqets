using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.EntityFrameworkCore.DependencyInjection;
using Pinqponq.Identity.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    private const string UnusedConnectionString = "Host=refresh-tokens-db;Database=refresh_tokens;Username=app;Password=app";

    [Fact]
    public void Store_is_registered_as_the_scoped_refresh_token_store()
    {
        using var serviceProvider = CreateServices().BuildServiceProvider();

        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();

        Assert.IsType<EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>>(firstStore);
        Assert.Same(firstStore, firstScope.ServiceProvider.GetRequiredService<IRefreshTokenStore>());
        Assert.NotSame(firstStore, secondScope.ServiceProvider.GetRequiredService<IRefreshTokenStore>());
    }

    [Fact]
    public void System_clock_is_registered_when_the_application_has_none()
    {
        using var serviceProvider = CreateServices().BuildServiceProvider();

        Assert.Same(TimeProvider.System, serviceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Clock_of_the_application_is_kept()
    {
        var timeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        AddStore(services);
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Same(timeProvider, serviceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Configure_sets_the_store_options()
    {
        Action<RefreshToken, OwnedRefreshTokenRecord> onRecordCreated = (_, record) => record.OwnerId = 7;
        using var serviceProvider = CreateServices(storeOptions => storeOptions.OnRecordCreated = onRecordCreated)
            .BuildServiceProvider();

        var storeOptions = serviceProvider.GetRequiredService<IOptions<EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>>>().Value;

        Assert.Same(onRecordCreated, storeOptions.OnRecordCreated);
    }

    [Fact]
    public void Default_OnRecordCreated_leaves_the_record_unchanged()
    {
        using var serviceProvider = CreateServices().BuildServiceProvider();
        var storeOptions = serviceProvider.GetRequiredService<IOptions<EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>>>().Value;
        var record = new OwnedRefreshTokenRecord();

        storeOptions.OnRecordCreated(new RefreshToken { Subject = "user-1" }, record);

        Assert.Equal(0, record.OwnerId);
        Assert.Equal(string.Empty, record.Subject);
    }

    [Fact]
    public void Refresh_token_service_of_Pinqponq_Identity_uses_the_store()
    {
        var services = CreateServices();
        services.AddPinqponqIdentity(jwtOptions =>
        {
            jwtOptions.Issuer = "issuer";
            jwtOptions.Audience = "audience";
            jwtOptions.SymmetricKey = "0123456789abcdef0123456789abcdef";
        });
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        Assert.IsType<RefreshTokenService>(refreshTokenService);
    }

    private static ServiceCollection CreateServices(
        Action<EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>>? configure = null)
    {
        var services = new ServiceCollection();
        AddStore(services, configure);
        return services;
    }

    private static void AddStore(
        IServiceCollection services,
        Action<EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>>? configure = null)
    {
        services.AddDbContext<TestRefreshTokenDbContext>(contextOptions => contextOptions.UseNpgsql(UnusedConnectionString));
        services.AddPinqponqEntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>(configure);
    }
}
