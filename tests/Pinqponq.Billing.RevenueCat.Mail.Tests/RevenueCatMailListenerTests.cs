using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

public sealed class RevenueCatMailListenerTests
{
    private readonly RecordingEmailSender _emailSender = new();
    private readonly FixedRecipientResolver _recipientResolver = new();
    private readonly RecordingObserver _observer = new();
    private readonly CapturingLogger<RevenueCatMailListener> _logger = new();
    private readonly RevenueCatMailOptions _options = new();

    public RevenueCatMailListenerTests()
    {
        _options.Brand.Name = "Example";
    }

    private RevenueCatMailListener CreateListener() => new(
        _recipientResolver,
        new RevenueCatMailRenderer(),
        _emailSender,
        Options.Create(_options),
        _observer,
        _logger);

    private static RevenueCatEventContext Context(
        string type,
        string? subscriberId = "21",
        bool isPremiumEntitlement = true) =>
        new(new RevenueCatEvent { Id = "evt-1", Type = type }, subscriberId, isPremiumEntitlement);

    private RevenueCatWebhookLog SingleLog() => Assert.Single(_observer.Logs);

    [Fact]
    public async Task Initial_purchase_sends_the_rendered_mail_to_the_resolved_recipient()
    {
        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        var message = Assert.Single(_emailSender.Messages);
        Assert.Equal("subscriber@example.com", message.To);
        Assert.Equal("Your Example subscription is active", message.Subject);
        Assert.True(message.IsBodyHtml);
        Assert.Contains("Thanks for subscribing", message.Body);
        Assert.Contains("Your Example subscription is now active.", message.PlainTextBody);
        Assert.Equal(["21"], _recipientResolver.ResolvedSubscriberIds);
    }

    [Fact]
    public async Task Sent_mail_is_logged_with_event_and_language()
    {
        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Sent, log.EventName);
        Assert.Equal(RevenueCatWebhookLogLevel.Info, log.Level);
        Assert.Equal("evt-1", log.Data![RevenueCatMailLogKeys.EventId]);
        Assert.Equal(RevenueCatEventType.InitialPurchase, log.Data[RevenueCatMailLogKeys.EventType]);
        Assert.Equal("21", log.Data[RevenueCatMailLogKeys.SubscriberId]);
        Assert.Equal(nameof(RevenueCatMailEvent.InitialPurchase), log.Data[RevenueCatMailLogKeys.MailEvent]);
        Assert.Equal("en", log.Data[RevenueCatMailLogKeys.Language]);
        Assert.Equal("RevenueCat subscription mail sent.", log.Message);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("sent"));
    }

    [Theory]
    [InlineData(RevenueCatEventType.Renewal)]
    [InlineData(RevenueCatEventType.Cancellation)]
    [InlineData(RevenueCatEventType.BillingIssue)]
    [InlineData(RevenueCatEventType.Expiration)]
    public async Task Events_disabled_by_default_send_nothing(string type)
    {
        await CreateListener().OnEventProcessed(Context(type));

        Assert.Empty(_emailSender.Messages);
        Assert.Empty(_recipientResolver.ResolvedSubscriberIds);
        Assert.Empty(_observer.Logs);
    }

    [Theory]
    [InlineData(RevenueCatEventType.Renewal, RevenueCatMailEvent.Renewal, "Your Example subscription was renewed")]
    [InlineData(RevenueCatEventType.Cancellation, RevenueCatMailEvent.Cancellation, "Your Example subscription will not renew")]
    [InlineData(RevenueCatEventType.BillingIssue, RevenueCatMailEvent.BillingIssue, "Payment problem with your Example subscription")]
    [InlineData(RevenueCatEventType.Expiration, RevenueCatMailEvent.Expiration, "Your Example subscription has ended")]
    public async Task Enabled_event_sends_its_own_text(string type, RevenueCatMailEvent mailEvent, string subject)
    {
        _options.Events[mailEvent].Enabled = true;

        await CreateListener().OnEventProcessed(Context(type));

        Assert.Equal(subject, Assert.Single(_emailSender.Messages).Subject);
    }

    [Theory]
    [InlineData(RevenueCatEventType.ProductChange)]
    [InlineData(RevenueCatEventType.Uncancellation)]
    [InlineData(RevenueCatEventType.NonRenewingPurchase)]
    [InlineData(RevenueCatEventType.Transfer)]
    [InlineData(RevenueCatEventType.SubscriberAlias)]
    [InlineData("SOMETHING_NEW")]
    public async Task Events_without_a_mail_send_nothing_even_when_everything_is_enabled(string type)
    {
        foreach (var eventOptions in _options.Events.Values)
        {
            eventOptions.Enabled = true;
        }

        await CreateListener().OnEventProcessed(Context(type));

        Assert.Empty(_emailSender.Messages);
        Assert.Empty(_observer.Logs);
    }

    [Fact]
    public async Task Event_removed_from_the_options_sends_nothing()
    {
        _options.Events.Remove(RevenueCatMailEvent.InitialPurchase);

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Empty(_emailSender.Messages);
    }

    [Fact]
    public async Task Event_without_the_premium_entitlement_is_skipped()
    {
        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase, isPremiumEntitlement: false));

        Assert.Empty(_emailSender.Messages);
        Assert.Empty(_recipientResolver.ResolvedSubscriberIds);
        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Skipped, log.EventName);
        Assert.Equal(RevenueCatMailSkipReasons.EntitlementNotPremium, log.Data![RevenueCatMailLogKeys.Reason]);
    }

    [Fact]
    public async Task Premium_entitlement_is_not_needed_when_the_option_is_off()
    {
        _options.RequirePremiumEntitlement = false;

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase, isPremiumEntitlement: false));

        Assert.Single(_emailSender.Messages);
    }

    [Fact]
    public async Task Unresolved_subscriber_is_skipped_and_logged()
    {
        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase, subscriberId: null));

        Assert.Empty(_emailSender.Messages);
        Assert.Empty(_recipientResolver.ResolvedSubscriberIds);
        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Skipped, log.EventName);
        Assert.Equal(RevenueCatMailSkipReasons.SubscriberUnresolved, log.Data![RevenueCatMailLogKeys.Reason]);
    }

    [Fact]
    public async Task Unresolved_recipient_is_skipped_and_logged()
    {
        _recipientResolver.Recipient = null;

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Empty(_emailSender.Messages);
        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Skipped, log.EventName);
        Assert.Equal(RevenueCatWebhookLogLevel.Info, log.Level);
        Assert.Equal(RevenueCatMailSkipReasons.RecipientUnresolved, log.Data![RevenueCatMailLogKeys.Reason]);
        Assert.Equal("RevenueCat subscription mail skipped.", log.Message);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("skipped"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-address")]
    public async Task Invalid_recipient_address_is_skipped_and_logged(string address)
    {
        _recipientResolver.Recipient = new RevenueCatMailRecipient(address, null);

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Empty(_emailSender.Messages);
        Assert.Equal(RevenueCatMailSkipReasons.RecipientAddressInvalid, SingleLog().Data![RevenueCatMailLogKeys.Reason]);
    }

    [Fact]
    public async Task Send_failure_is_logged_and_not_thrown()
    {
        var failure = new InvalidOperationException("smtp down");
        _emailSender.ExceptionToThrow = failure;

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Failed, log.EventName);
        Assert.Equal(RevenueCatWebhookLogLevel.Warning, log.Level);
        Assert.Equal(typeof(InvalidOperationException).FullName, log.Data![RevenueCatMailLogKeys.ExceptionType]);
        Assert.Equal("RevenueCat subscription mail failed.", log.Message);
        var error = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, error.Exception);
        Assert.Contains("InitialPurchase mail failed for subscriber 21 (event evt-1)", error.Message);
    }

    [Fact]
    public async Task Resolver_failure_is_logged_and_not_thrown()
    {
        _recipientResolver.ExceptionToThrow = new TimeoutException();

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Equal(RevenueCatMailLogEvents.Failed, SingleLog().EventName);
    }

    [Fact]
    public async Task Cancellation_of_the_request_is_logged_and_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _emailSender.ExceptionToThrow = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase), cancellation.Token));

        var log = SingleLog();
        Assert.Equal(RevenueCatMailLogEvents.Failed, log.EventName);
        Assert.Equal(typeof(OperationCanceledException).FullName, log.Data![RevenueCatMailLogKeys.ExceptionType]);
    }

    [Fact]
    public async Task Timeout_that_is_not_a_request_cancellation_is_logged_as_a_failure()
    {
        _emailSender.ExceptionToThrow = new TaskCanceledException("smtp timeout");

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase), CancellationToken.None);

        Assert.Equal(RevenueCatMailLogEvents.Failed, SingleLog().EventName);
    }

    [Fact]
    public async Task Null_context_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateListener().OnEventProcessed(null!));
    }

    [Theory]
    [InlineData("tr", "tr", "Abonelik aktif")]
    [InlineData("TR", "TR", "Abonelik aktif")]
    [InlineData("tr-TR", "tr", "Abonelik aktif")]
    [InlineData("de", "en", "Your Example subscription is active")]
    [InlineData("de-DE", "en", "Your Example subscription is active")]
    [InlineData(null, "en", "Your Example subscription is active")]
    [InlineData("", "en", "Your Example subscription is active")]
    public async Task Language_falls_back_to_the_base_language_then_the_default(string? recipientLanguage, string expectedLanguage, string expectedSubject)
    {
        _options.Texts["tr"] = new RevenueCatMailTexts
        {
            Events = { [RevenueCatMailEvent.InitialPurchase] = new RevenueCatMailText { Subject = "Abonelik aktif", Title = "Teşekkürler" } },
        };
        _recipientResolver.Recipient = new RevenueCatMailRecipient("subscriber@example.com", recipientLanguage);

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Equal(expectedSubject, Assert.Single(_emailSender.Messages).Subject);
        Assert.Equal(expectedLanguage, SingleLog().Data![RevenueCatMailLogKeys.Language]);
    }

    [Fact]
    public async Task Language_without_a_text_for_the_event_uses_the_default_language()
    {
        _options.Events[RevenueCatMailEvent.Renewal].Enabled = true;
        _options.Texts["tr"] = new RevenueCatMailTexts
        {
            Events = { [RevenueCatMailEvent.InitialPurchase] = new RevenueCatMailText { Subject = "Abonelik aktif", Title = "Teşekkürler" } },
        };
        _recipientResolver.Recipient = new RevenueCatMailRecipient("subscriber@example.com", "tr");

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.Renewal));

        Assert.Equal("Your Example subscription was renewed", Assert.Single(_emailSender.Messages).Subject);
    }

    [Fact]
    public async Task Missing_default_text_is_logged_as_a_failure()
    {
        _options.Texts.Clear();

        await CreateListener().OnEventProcessed(Context(RevenueCatEventType.InitialPurchase));

        Assert.Empty(_emailSender.Messages);
        Assert.Equal(RevenueCatMailLogEvents.Failed, SingleLog().EventName);
        var error = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Error);
        var exception = Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains("InitialPurchase", exception.Message);
        Assert.Contains("'en'", exception.Message);
    }
}
