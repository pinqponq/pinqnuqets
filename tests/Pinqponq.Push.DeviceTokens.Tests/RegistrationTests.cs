using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Push.DeviceTokens.DependencyInjection;
using Pinqponq.Push.Fcm;
using Xunit;

namespace Pinqponq.Push.DeviceTokens.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RegistrationTests(DeviceTokenDatabaseFixture database)
{
    private ServiceProvider BuildProvider(Action<DeviceTokenOptions>? configure = null, Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseNpgsql(database.ConnectionString));
        before?.Invoke(services);
        services.AddPinqponqDeviceTokens<TestDbContext>(configure);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Registry_is_resolved_and_accepts_the_default_platforms()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<IDeviceTokenRegistry>();
        var token = $"token-{Guid.NewGuid():N}";
        await registry.Register("user", token, "WEB");

        Assert.Equal("web", (await Rows(token)).Platform);
        Assert.Equal(1, await registry.RemoveTokens([token]));
    }

    [Fact]
    public async Task Tokens_fcm_reports_invalid_leave_the_store()
    {
        var dead = $"dead-{Guid.NewGuid():N}";
        var alive = $"alive-{Guid.NewGuid():N}";
        await using var provider = BuildProvider(before: services => services.AddScoped<IFcmInvalidTokenHandler, KeepEverythingHandler>());
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceTokenRegistry>();
        await registry.Register("user", dead, "ios");
        await registry.Register("user", alive, "ios");

        await scope.ServiceProvider.GetRequiredService<IFcmInvalidTokenHandler>().HandleInvalidTokens([dead]);

        var tokens = await registry.GetTokens("user");
        Assert.DoesNotContain(dead, tokens);
        Assert.Contains(alive, tokens);
        await registry.RemoveTokens([alive]);
    }

    [Fact]
    public async Task Fcm_handler_is_left_alone_when_the_option_is_off()
    {
        await using var provider = BuildProvider(
            o => o.RemoveTokensFcmReportsInvalid = false,
            services => services.AddScoped<IFcmInvalidTokenHandler, KeepEverythingHandler>());
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<KeepEverythingHandler>(scope.ServiceProvider.GetRequiredService<IFcmInvalidTokenHandler>());
    }

    [Fact]
    public void Non_positive_token_length_fails_at_registration()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddPinqponqDeviceTokens<TestDbContext>(o => o.MaxTokenLength = 0));

        Assert.Contains("MaxTokenLength", exception.Message);
    }

    [Fact]
    public void Non_positive_device_identifier_length_fails_at_registration()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddPinqponqDeviceTokens<TestDbContext>(o => o.MaxDeviceIdentifierLength = 0));

        Assert.Contains("MaxDeviceIdentifierLength", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_or_blank_platform_list_fails_at_registration(bool hasBlankName)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddPinqponqDeviceTokens<TestDbContext>(
            o => o.Platforms = hasBlankName ? ["ios", " "] : []));
    }

    [Fact]
    public async Task Configured_options_reach_the_registry_and_the_model_has_the_columns_it_needs()
    {
        await using var provider = BuildProvider(o => o.MaxTokenLength = 3);
        await using var scope = provider.CreateAsyncScope();

        Assert.Equal(3, scope.ServiceProvider.GetRequiredService<IOptions<DeviceTokenOptions>>().Value.MaxTokenLength);
        await Assert.ThrowsAsync<ArgumentException>(
            () => scope.ServiceProvider.GetRequiredService<IDeviceTokenRegistry>().Register("u", "abcd", "ios"));
    }

    [Fact]
    public void Model_requires_user_token_and_platform_and_indexes_user_and_device()
    {
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(DeviceToken))!;

        Assert.Equal(nameof(DeviceToken.Id), entity.FindPrimaryKey()!.Properties.Single().Name);
        Assert.False(entity.FindProperty(nameof(DeviceToken.UserId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(DeviceToken.Token))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(DeviceToken.Platform))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DeviceToken.DeviceIdentifier))!.IsNullable);
        var composite = entity.GetIndexes().Single(i => i.Properties.Count == 2);
        Assert.Equal([nameof(DeviceToken.UserId), nameof(DeviceToken.DeviceIdentifier)], composite.Properties.Select(p => p.Name));
        Assert.False(composite.IsUnique);
    }

    [Fact]
    public void Empty_platform_list_message_names_the_option()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddPinqponqDeviceTokens<TestDbContext>(o => o.Platforms = []));

        Assert.Contains("Platforms", exception.Message);
    }

    [Fact]
    public void Lengths_of_one_are_accepted_at_registration()
    {
        new ServiceCollection().AddPinqponqDeviceTokens<TestDbContext>(o =>
        {
            o.MaxTokenLength = 1;
            o.MaxDeviceIdentifierLength = 1;
        });
    }

    [Fact]
    public void Missing_builders_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqDeviceTokens<TestDbContext>());
        Assert.Throws<ArgumentNullException>(() => ((ModelBuilder)null!).ApplyPinqponqDeviceTokens());
        Assert.Throws<ArgumentNullException>(
            () => ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)null!).MapPinqponqDeviceTokenEndpoints());
    }

    [Fact]
    public void Table_name_and_schema_are_options_of_the_model()
    {
        var builder = new DbContextOptionsBuilder<NamedTableContext>().UseNpgsql("Host=unused");
        using var context = new NamedTableContext(builder.Options);

        var entity = context.Model.FindEntityType(typeof(DeviceToken))!;

        Assert.Equal("push_tokens", entity.GetTableName());
        Assert.Equal("app", entity.GetSchema());
        Assert.True(entity.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(DeviceToken.Token)).IsUnique);
    }

    [Fact]
    public void Default_table_name_is_DeviceTokens_without_a_schema()
    {
        using var context = database.CreateContext();

        var entity = context.Model.FindEntityType(typeof(DeviceToken))!;

        Assert.Equal("DeviceTokens", entity.GetTableName());
        Assert.Null(entity.GetSchema());
    }

    [Fact]
    public void Defaults_are_neutral()
    {
        var options = new DeviceTokenOptions();

        Assert.Equal(4096, options.MaxTokenLength);
        Assert.Equal(256, options.MaxDeviceIdentifierLength);
        Assert.Equal(["android", "ios", "web"], options.Platforms);
        Assert.True(options.RemoveTokensFcmReportsInvalid);
        Assert.Equal("/device-tokens", new DeviceTokenEndpointOptions().Route);
        Assert.Equal(System.Security.Claims.ClaimTypes.NameIdentifier, new DeviceTokenEndpointOptions().UserIdClaimType);
    }

    private async Task<DeviceToken> Rows(string token)
    {
        await using var context = database.CreateContext();
        return await context.Set<DeviceToken>().AsNoTracking().SingleAsync(r => r.Token == token);
    }

    private sealed class KeepEverythingHandler : IFcmInvalidTokenHandler
    {
        public Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NamedTableContext(DbContextOptions<NamedTableContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyPinqponqDeviceTokens(o =>
            {
                o.TableName = "push_tokens";
                o.Schema = "app";
            });
    }
}
