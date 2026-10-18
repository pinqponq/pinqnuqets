using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pinqponq.Billing.RevenueCat.DependencyInjection;
using Pinqponq.Billing.RevenueCat.Mail.DependencyInjection;
using Pinqponq.Mail;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private const string PremiumEntitlementId = "premium";

    private readonly RecordingEmailSender _emailSender = new();
    private readonly InMemoryRevenueCatStore _store = new();

    private ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IEmailSender>(_emailSender);
        services.AddSingleton<IRevenueCatSubscriptionStore>(_store);
        services.AddPinqponqRevenueCat<InMemoryRevenueCatStore>(options =>
        {
            options.WebhookAuthHeader = "Bearer x";
            options.PremiumEntitlementId = PremiumEntitlementId;
        });
        return services;
    }

    private static void ConfigureBrand(RevenueCatMailOptions options) => options.Brand.Name = "Example";

    private static RevenueCatWebhookPayload Payload(string eventId, string type) => new()
    {
        Event = new RevenueCatEvent
        {
            Id = eventId,
            Type = type,
            AppUserId = "21",
            EntitlementIds = [PremiumEntitlementId],
            ExpirationAtMs = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds(),
        },
    };

    [Fact]
    public void Registers_listener_renderer_and_resolver()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var listener = Assert.Single(scope.ServiceProvider.GetServices<IRevenueCatEventListener>());
        Assert.IsType<RevenueCatMailListener>(listener);
        Assert.IsType<RevenueCatMailRenderer>(provider.GetRequiredService<IRevenueCatMailRenderer>());
        Assert.IsType<FixedRecipientResolver>(scope.ServiceProvider.GetRequiredService<IRevenueCatMailRecipientResolver>());
        Assert.Equal("Example", provider.GetRequiredService<IOptions<RevenueCatMailOptions>>().Value.Brand.Name);
    }

    [Fact]
    public void Calling_twice_registers_the_listener_once()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IRevenueCatEventListener>());
    }

    [Fact]
    public void Renderer_registered_by_the_product_wins()
    {
        var services = NewServices();
        services.AddSingleton<IRevenueCatMailRenderer, FixedRenderer>();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FixedRenderer>(provider.GetRequiredService<IRevenueCatMailRenderer>());
    }

    [Fact]
    public void Invalid_options_fail_validation_on_first_use()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(_ => { });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RevenueCatMailOptions>>().Value);

        Assert.Contains("Brand.Name", exception.Message);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => Pinqponq.Billing.RevenueCat.Mail.DependencyInjection.ServiceCollectionExtensions.AddPinqponqRevenueCatMail<FixedRecipientResolver>(null!, ConfigureBrand));
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddPinqponqRevenueCatMail<FixedRecipientResolver>(null!));
    }

    [Fact]
    public async Task Handled_initial_purchase_sends_one_mail_and_its_duplicate_none()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IRevenueCatEventHandler>();

        await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase));
        await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase));

        var message = Assert.Single(_emailSender.Messages);
        Assert.Equal("Your Example subscription is active", message.Subject);
    }

    [Fact]
    public async Task Test_event_sends_nothing()
    {
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(options =>
        {
            ConfigureBrand(options);
            foreach (var eventOptions in options.Events.Values)
            {
                eventOptions.Enabled = true;
            }
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IRevenueCatEventHandler>()
            .Handle(Payload("evt-test", RevenueCatEventType.Test));

        Assert.Empty(_emailSender.Messages);
    }

    [Fact]
    public async Task Mail_failure_does_not_fail_the_webhook()
    {
        _emailSender.ExceptionToThrow = new InvalidOperationException("smtp down");
        var services = NewServices();
        services.AddPinqponqRevenueCatMail<FixedRecipientResolver>(ConfigureBrand);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var handled = await scope.ServiceProvider.GetRequiredService<IRevenueCatEventHandler>()
            .Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase));

        Assert.True(handled);
    }

    private sealed class FixedRenderer : IRevenueCatMailRenderer
    {
        public RevenueCatRenderedMail Render(RevenueCatMailRenderRequest request) => new("s", "<p>b</p>", "b");
    }
}
