using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Tests;

public sealed class RevenueCatEventHandlerTests
{
    private const string PremiumEntitlementId = "premium";
    private const string UserId = "21";

    private readonly InMemoryRevenueCatStore _store = new();
    private readonly RecordingObserver _observer = new();

    private RevenueCatEventHandler CreateSut() => new(
        _store,
        Options.Create(new RevenueCatOptions { WebhookAuthHeader = "Bearer test", PremiumEntitlementId = PremiumEntitlementId }),
        _observer,
        NullLogger<RevenueCatEventHandler>.Instance);

    private static RevenueCatWebhookPayload Payload(
        string id,
        string type,
        long eventTimestampMs = 1782201714434,
        string originalTransactionId = "txn-1",
        long expirationAtMs = 1784793702131,
        Action<RevenueCatEvent>? customize = null)
    {
        var revenueCatEvent = new RevenueCatEvent
        {
            Id = id,
            Type = type,
            EventTimestampMs = eventTimestampMs,
            AppUserId = UserId,
            Aliases = ["$RCAnonymousID:e5f4adead25641fb861877b49f049594", UserId],
            EntitlementIds = [PremiumEntitlementId],
            ProductId = "pro_monthly",
            Store = "PLAY_STORE",
            OriginalTransactionId = originalTransactionId,
            TransactionId = originalTransactionId,
            ExpirationAtMs = expirationAtMs,
        };
        customize?.Invoke(revenueCatEvent);
        return new RevenueCatWebhookPayload { ApiVersion = "1.0", Event = revenueCatEvent };
    }

    private static DateTime Utc(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;

    [Fact]
    public async Task Resubscribe_keeps_premium_active_when_expiration_arrives_for_superseded_transaction()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("8B67408A", RevenueCatEventType.InitialPurchase, 1782201714434, "GPA.3312", 1784793702131));
        await sut.Handle(Payload("4FD7CF9D", RevenueCatEventType.Cancellation, 1782298800057, "GPA.3312", 1784793702131));
        await sut.Handle(Payload("468E2302", RevenueCatEventType.InitialPurchase, 1782724778486, "GPA.3353", 1784794930630));
        await sut.Handle(Payload("BD1226B3", RevenueCatEventType.Expiration, 1782724779138, "GPA.3312", 1782724763288));
        await sut.Handle(Payload("0172B612", RevenueCatEventType.Cancellation, 1783076301485, "GPA.3353", 1784794930630));
        await sut.Handle(Payload("59E34F4E", RevenueCatEventType.InitialPurchase, 1783578107581, "GPA.3336", 1784795036672));
        await sut.Handle(Payload("7F6071CB", RevenueCatEventType.Expiration, 1783578108199, "GPA.3353", 1783578095318));

        var subscription = _store.Get(UserId);
        Assert.NotNull(subscription);
        Assert.True(subscription.IsActive);
        Assert.Equal("GPA.3336", subscription.OriginalTransactionId);
        Assert.Equal(Utc(1784795036672), subscription.ExpiresAt);
    }

    [Fact]
    public async Task Cancellation_keeps_subscription_active_until_expiry()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));
        await sut.Handle(Payload("e2", RevenueCatEventType.Cancellation));

        var subscription = _store.Get(UserId)!;
        Assert.True(subscription.IsActive);
        Assert.False(subscription.WillRenew);
        Assert.Equal(RevenueCatEventType.Cancellation, subscription.LastEventType);
    }

    [Fact]
    public async Task Expiration_for_current_transaction_deactivates_subscription()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));
        await sut.Handle(Payload("e2", RevenueCatEventType.Expiration));

        var subscription = _store.Get(UserId)!;
        Assert.False(subscription.IsActive);
        Assert.False(subscription.WillRenew);
        Assert.False(subscription.IsInGracePeriod);
    }

    [Fact]
    public async Task Superseded_event_is_observed_and_still_recorded_as_processed()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, originalTransactionId: "new"));

        var handled = await sut.Handle(Payload("e2", RevenueCatEventType.Expiration, originalTransactionId: "old"));

        Assert.True(handled);
        Assert.True(_store.Get(UserId)!.IsActive);
        Assert.Contains(_store.Processed, entry => entry.EventId == "e2");
        var log = Assert.Single(_observer.Logs, log => log.EventName == RevenueCatWebhookLogEvents.EventSuperseded);
        Assert.Equal(RevenueCatWebhookLogLevel.Info, log.Level);
        Assert.Equal("e2", log.Data!["eventId"]);
    }

    [Fact]
    public async Task Event_older_than_stored_expiry_is_superseded_even_for_the_same_transaction()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, expirationAtMs: 2000));

        await sut.Handle(Payload("e2", RevenueCatEventType.Expiration, expirationAtMs: 1000));

        Assert.True(_store.Get(UserId)!.IsActive);
    }

    [Fact]
    public async Task Initial_purchase_creates_subscription_from_the_event()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));

        Assert.True(handled);
        var subscription = _store.Get(UserId)!;
        Assert.Equal(UserId, subscription.RevenueCatAppUserId);
        Assert.Equal(PremiumEntitlementId, subscription.EntitlementId);
        Assert.Equal("pro_monthly", subscription.ProductId);
        Assert.Equal("PLAY_STORE", subscription.Store);
        Assert.True(subscription.IsActive);
        Assert.True(subscription.WillRenew);
        Assert.False(subscription.IsInGracePeriod);
        Assert.Equal("txn-1", subscription.OriginalTransactionId);
        Assert.Equal(RevenueCatEventType.InitialPurchase, subscription.LastEventType);
        Assert.Equal(Utc(1782201714434), subscription.LastEventAt);
        Assert.Equal(Utc(1784793702131), subscription.ExpiresAt);
        Assert.Equal([("e1", RevenueCatEventType.InitialPurchase)], _store.Processed);
    }

    [Fact]
    public async Task Product_change_takes_the_new_product_id()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));

        await sut.Handle(Payload("e2", RevenueCatEventType.ProductChange, customize: e => e.NewProductId = "pro_yearly"));

        Assert.Equal("pro_yearly", _store.Get(UserId)!.ProductId);
    }

    [Fact]
    public async Task Uncancellation_turns_renewal_back_on()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));
        await sut.Handle(Payload("e2", RevenueCatEventType.Cancellation));

        await sut.Handle(Payload("e3", RevenueCatEventType.Uncancellation));

        Assert.True(_store.Get(UserId)!.WillRenew);
    }

    [Fact]
    public async Task Activation_with_other_entitlement_records_an_inactive_subscription()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, customize: e => e.EntitlementIds = ["other"]));

        Assert.False(_store.Get(UserId)!.IsActive);
        Assert.Contains(_observer.Logs, log => log.EventName == RevenueCatWebhookLogEvents.EntitlementNotPremium);
    }

    [Fact]
    public async Task Activation_without_entitlements_is_not_premium()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, customize: e => e.EntitlementIds = null));

        Assert.False(_store.Get(UserId)!.IsActive);
    }

    [Fact]
    public async Task Billing_issue_marks_grace_period_and_keeps_premium()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));

        await sut.Handle(Payload("e2", RevenueCatEventType.BillingIssue));

        var subscription = _store.Get(UserId)!;
        Assert.True(subscription.IsInGracePeriod);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task Billing_issue_for_unknown_subscriber_changes_nothing_but_is_recorded()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.BillingIssue));

        Assert.True(handled);
        Assert.Null(_store.Get(UserId));
        Assert.Single(_store.Processed);
    }

    [Fact]
    public async Task Expiration_for_unknown_subscriber_changes_nothing()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.Expiration));

        Assert.Null(_store.Get(UserId));
    }

    [Fact]
    public async Task Cancellation_for_unknown_subscriber_is_observed_as_a_warning()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.Cancellation));

        Assert.True(handled);
        var log = Assert.Single(_observer.Logs);
        Assert.Equal(RevenueCatWebhookLogEvents.CancellationUnknownUser, log.EventName);
        Assert.Equal(RevenueCatWebhookLogLevel.Warning, log.Level);
        Assert.Equal(UserId, log.Data!["userId"]);
    }

    [Fact]
    public async Task Subscriber_is_resolved_from_aliases_when_app_user_id_is_anonymous()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, customize: e => e.AppUserId = "$RCAnonymousID:abc"));

        var subscription = _store.Get(UserId)!;
        Assert.Equal("$RCAnonymousID:abc", subscription.RevenueCatAppUserId);
    }

    [Fact]
    public async Task Unresolvable_subscriber_is_observed_and_the_event_is_still_recorded()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase, customize: e =>
        {
            e.AppUserId = "$RCAnonymousID:abc";
            e.Aliases = ["$RCAnonymousID:def"];
        }));

        Assert.True(handled);
        Assert.Null(_store.Get(UserId));
        Assert.Single(_store.Processed);
        var log = Assert.Single(_observer.Logs);
        Assert.Equal(RevenueCatWebhookLogEvents.UserUnresolved, log.EventName);
        Assert.Equal("$RCAnonymousID:abc", log.Data!["appUserId"]);
    }

    [Fact]
    public async Task Duplicate_event_is_acknowledged_without_a_second_change()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));
        await sut.Handle(Payload("e2", RevenueCatEventType.Cancellation));
        var commits = _store.CommitCount;

        var handled = await sut.Handle(Payload("e2", RevenueCatEventType.Expiration));

        Assert.True(handled);
        Assert.True(_store.Get(UserId)!.IsActive);
        Assert.Equal(commits, _store.CommitCount);
        Assert.Equal(RevenueCatWebhookLogEvents.EventDuplicate, _observer.Logs.Single().EventName);
    }

    [Fact]
    public async Task Test_event_is_recorded_and_acknowledged()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.Test));

        Assert.True(handled);
        Assert.Null(_store.Get(UserId));
        Assert.Equal([("e1", RevenueCatEventType.Test)], _store.Processed);
        Assert.Equal(1, _store.CommitCount);
        Assert.Equal(RevenueCatWebhookLogEvents.TestReceived, _observer.Logs.Single().EventName);
    }

    [Fact]
    public async Task Subscriber_alias_event_changes_no_subscription_but_is_recorded()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.SubscriberAlias));

        Assert.True(handled);
        Assert.Null(_store.Get(UserId));
        Assert.Single(_store.Processed);
        Assert.Equal(RevenueCatWebhookLogEvents.SubscriberAliasReceived, _observer.Logs.Single().EventName);
    }

    [Fact]
    public async Task Unknown_event_type_is_observed_and_recorded()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", "SOMETHING_NEW"));

        Assert.True(handled);
        Assert.Single(_store.Processed);
        var log = Assert.Single(_observer.Logs);
        Assert.Equal(RevenueCatWebhookLogEvents.EventUnhandled, log.EventName);
        Assert.Equal("SOMETHING_NEW", log.Data!["eventType"]);
    }

    [Theory]
    [InlineData(null, "INITIAL_PURCHASE")]
    [InlineData("", "INITIAL_PURCHASE")]
    [InlineData("  ", "INITIAL_PURCHASE")]
    [InlineData("e1", null)]
    [InlineData("e1", " ")]
    public async Task Event_without_id_or_type_is_rejected(string? id, string? type)
    {
        var sut = CreateSut();

        var handled = await sut.Handle(new RevenueCatWebhookPayload { Event = new RevenueCatEvent { Id = id, Type = type } });

        Assert.False(handled);
        Assert.Empty(_store.Processed);
        Assert.Equal(0, _store.CommitCount);
        Assert.Equal(RevenueCatWebhookLogEvents.PayloadInvalid, _observer.Logs.Single().EventName);
    }

    [Fact]
    public async Task Payload_without_event_is_rejected()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(new RevenueCatWebhookPayload());

        Assert.False(handled);
        Assert.Equal(0, _store.CommitCount);
    }

    [Fact]
    public async Task Transfer_deactivates_old_subscribers_and_activates_new_ones()
    {
        var sut = CreateSut();
        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));

        await sut.Handle(Payload("e2", RevenueCatEventType.Transfer, customize: e =>
        {
            e.TransferredFrom = [UserId, "$RCAnonymousID:gone", "999"];
            e.TransferredTo = ["22", "$RCAnonymousID:skipped"];
        }));

        var old = _store.Get(UserId)!;
        Assert.False(old.IsActive);
        Assert.False(old.WillRenew);
        Assert.Equal(RevenueCatEventType.Transfer, old.LastEventType);
        Assert.Null(_store.Get("999"));

        var moved = _store.Get("22")!;
        Assert.True(moved.IsActive);
        Assert.True(moved.WillRenew);
        Assert.Equal("pro_monthly", moved.ProductId);
        Assert.Equal("PLAY_STORE", moved.Store);
        Assert.Equal("txn-1", moved.OriginalTransactionId);
        Assert.Equal(Utc(1784793702131), moved.ExpiresAt);

        var skipped = Assert.Single(_observer.Logs, log => log.EventName == RevenueCatWebhookLogEvents.TransferSkipped);
        Assert.Equal("$RCAnonymousID:gone", skipped.Data!["appUserId"]);
    }

    [Fact]
    public async Task Transfer_without_transferred_to_activates_the_event_app_user()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.Transfer));

        Assert.True(_store.Get(UserId)!.IsActive);
    }

    [Fact]
    public async Task Transfer_without_any_target_changes_nothing()
    {
        var sut = CreateSut();

        var handled = await sut.Handle(Payload("e1", RevenueCatEventType.Transfer, customize: e => e.AppUserId = null));

        Assert.True(handled);
        Assert.Null(_store.Get(UserId));
    }

    [Fact]
    public async Task Transfer_to_a_non_premium_entitlement_leaves_the_target_inactive()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.Transfer, customize: e =>
        {
            e.EntitlementIds = ["other"];
            e.TransferredTo = ["22"];
        }));

        Assert.False(_store.Get("22")!.IsActive);
    }

    [Fact]
    public async Task Subscription_changes_and_processed_event_are_committed_together()
    {
        var sut = CreateSut();

        await sut.Handle(Payload("e1", RevenueCatEventType.InitialPurchase));

        Assert.Equal(1, _store.CommitCount);
    }

    [Fact]
    public async Task Null_payload_is_an_argument_error()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.Handle(null!));
    }
}
