using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pinqponq.Billing.RevenueCat.DependencyInjection;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    private static void Configure(RevenueCatOptions options)
    {
        options.WebhookAuthHeader = "Bearer x";
        options.PremiumEntitlementId = "premium";
    }

    [Fact]
    public void Registers_handler_authenticator_and_store()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(Configure);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<RevenueCatEventHandler>(scope.ServiceProvider.GetRequiredService<IRevenueCatEventHandler>());
        Assert.IsType<InMemoryRevenueCatStore>(scope.ServiceProvider.GetRequiredService<IRevenueCatSubscriptionStore>());
        Assert.IsType<RevenueCatWebhookAuthenticator>(provider.GetRequiredService<IRevenueCatWebhookAuthenticator>());
        Assert.Equal("premium", provider.GetRequiredService<IOptions<RevenueCatOptions>>().Value.PremiumEntitlementId);
    }

    [Fact]
    public void Default_observer_ignores_logs()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(Configure);
        using var provider = services.BuildServiceProvider();

        var observer = provider.GetRequiredService<IRevenueCatWebhookObserver>();

        observer.Observe(new RevenueCatWebhookLog(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.TestReceived, "m", null));
    }

    [Fact]
    public void Observer_registered_by_the_product_wins_regardless_of_order()
    {
        var before = NewServices();
        before.AddSingleton<IRevenueCatWebhookObserver, RecordingObserver>();
        before.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(Configure);

        var after = NewServices();
        after.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(Configure);
        after.AddSingleton<IRevenueCatWebhookObserver, RecordingObserver>();

        foreach (var services in new[] { before, after })
        {
            using var provider = services.BuildServiceProvider();
            Assert.IsType<RecordingObserver>(provider.GetRequiredService<IRevenueCatWebhookObserver>());
        }
    }

    [Fact]
    public void Missing_required_option_fails_validation_on_first_use()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(options => options.WebhookAuthHeader = "Bearer x");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RevenueCatOptions>>().Value);

        Assert.Contains(nameof(RevenueCatOptions.PremiumEntitlementId), exception.Message);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqRevenueCat<InMemoryRevenueCatStore>(Configure));
        Assert.Throws<ArgumentNullException>(() => NewServices().AddPinqponqRevenueCat<InMemoryRevenueCatStore>(null!));
    }
}
