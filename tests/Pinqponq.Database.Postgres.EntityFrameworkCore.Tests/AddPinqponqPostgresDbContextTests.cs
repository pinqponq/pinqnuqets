using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Database.Postgres.EntityFrameworkCore.DependencyInjection;
using Xunit;

namespace Pinqponq.Database.Postgres.EntityFrameworkCore.Tests;

public sealed class AddPinqponqPostgresDbContextTests
{
    private const string OrdersConnectionString = "Host=orders-db;Database=orders;Username=app;Password=app";
    private const string AuditConnectionString = "Host=audit-db;Database=audit;Username=app;Password=app";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_connection_string_fails_at_registration(string? connectionString)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddPinqponqPostgresDbContext<OrdersDbContext>(connectionString));

        Assert.Contains(nameof(OrdersDbContext), exception.Message);
    }

    [Fact]
    public void Context_uses_npgsql_over_the_configured_data_source()
    {
        var dataSourceConfigurationCount = 0;
        var isNpgsqlConfigured = false;
        var services = new ServiceCollection();
        services.AddPinqponqPostgresDbContext<OrdersDbContext>(
            OrdersConnectionString,
            dataSource =>
            {
                dataSourceConfigurationCount++;
                dataSource.EnableDynamicJson(jsonbClrTypes: [typeof(Dictionary<string, string>)], jsonClrTypes: []);
            },
            _ => isNpgsqlConfigured = true);
        using var serviceProvider = services.BuildServiceProvider();

        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", firstContext.Database.ProviderName);
        Assert.Contains("Host=orders-db", firstContext.Database.GetDbConnection().ConnectionString);
        Assert.NotSame(firstContext, secondContext);
        Assert.Equal(1, dataSourceConfigurationCount);
        Assert.True(isNpgsqlConfigured);
    }

    [Fact]
    public void Data_source_is_shared_by_the_context_and_disposed_with_the_container()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgresDbContext<OrdersDbContext>(OrdersConnectionString);
        var serviceProvider = services.BuildServiceProvider();
        var firstDataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<OrdersDbContext>>().DataSource;
        var secondDataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<OrdersDbContext>>().DataSource;

        serviceProvider.Dispose();

        Assert.Same(firstDataSource, secondDataSource);
        Assert.Throws<ObjectDisposedException>(() => firstDataSource.CreateConnection().Open());
    }

    [Fact]
    public void Each_context_type_has_its_own_data_source()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgresDbContext<OrdersDbContext>(OrdersConnectionString);
        services.AddPinqponqPostgresDbContext<AuditDbContext>(AuditConnectionString);
        using var serviceProvider = services.BuildServiceProvider();

        var ordersDataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<OrdersDbContext>>().DataSource;
        var auditDataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<AuditDbContext>>().DataSource;

        Assert.Contains("Host=orders-db", ordersDataSource.ConnectionString);
        Assert.Contains("Host=audit-db", auditDataSource.ConnectionString);
    }

    [Fact]
    public void Registering_the_same_context_twice_keeps_the_first_data_source()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgresDbContext<OrdersDbContext>(OrdersConnectionString);
        services.AddPinqponqPostgresDbContext<OrdersDbContext>(AuditConnectionString);
        using var serviceProvider = services.BuildServiceProvider();

        var dataSource = serviceProvider.GetRequiredService<PostgresDbContextDataSource<OrdersDbContext>>().DataSource;

        Assert.Contains("Host=orders-db", dataSource.ConnectionString);
    }

    private sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options);

    private sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options);
}
