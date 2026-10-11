using System.Text.Json;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Tests;

public sealed class RevenueCatWebhookPayloadTests
{
    private const string Body = """
        {
          "api_version": "1.0",
          "event": {
            "id": "8B67408A",
            "type": "TRANSFER",
            "event_timestamp_ms": 1782201714434,
            "app_user_id": "21",
            "original_app_user_id": "20",
            "aliases": ["21", "$RCAnonymousID:abc"],
            "product_id": "pro_monthly",
            "new_product_id": "pro_yearly",
            "entitlement_ids": ["premium"],
            "store": "APP_STORE",
            "purchased_at_ms": 1782201700000,
            "expiration_at_ms": 1784793702131,
            "transaction_id": "t1",
            "original_transaction_id": "t0",
            "cancel_reason": "UNSUBSCRIBE",
            "grace_period_expiration_at_ms": 1784800000000,
            "transferred_from": ["20"],
            "transferred_to": ["21"],
            "unknown_future_field": true
          }
        }
        """;

    [Fact]
    public void Webhook_body_maps_every_field_by_its_wire_name()
    {
        var payload = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(Body)!;

        Assert.Equal("1.0", payload.ApiVersion);
        var e = payload.Event!;
        Assert.Equal("8B67408A", e.Id);
        Assert.Equal(RevenueCatEventType.Transfer, e.Type);
        Assert.Equal(1782201714434, e.EventTimestampMs);
        Assert.Equal("21", e.AppUserId);
        Assert.Equal("20", e.OriginalAppUserId);
        Assert.Equal(["21", "$RCAnonymousID:abc"], e.Aliases);
        Assert.Equal("pro_monthly", e.ProductId);
        Assert.Equal("pro_yearly", e.NewProductId);
        Assert.Equal(["premium"], e.EntitlementIds);
        Assert.Equal("APP_STORE", e.Store);
        Assert.Equal(1782201700000, e.PurchasedAtMs);
        Assert.Equal(1784793702131, e.ExpirationAtMs);
        Assert.Equal("t1", e.TransactionId);
        Assert.Equal("t0", e.OriginalTransactionId);
        Assert.Equal("UNSUBSCRIBE", e.CancelReason);
        Assert.Equal(1784800000000, e.GracePeriodExpirationAtMs);
        Assert.Equal(["20"], e.TransferredFrom);
        Assert.Equal(["21"], e.TransferredTo);
    }

    [Fact]
    public void Body_without_an_event_parses_with_a_null_event()
    {
        var payload = JsonSerializer.Deserialize<RevenueCatWebhookPayload>("""{"api_version":"1.0"}""")!;

        Assert.Null(payload.Event);
    }

    [Fact]
    public void Event_type_constants_match_the_wire_values()
    {
        Assert.Equal(
            ["INITIAL_PURCHASE", "RENEWAL", "PRODUCT_CHANGE", "CANCELLATION", "UNCANCELLATION", "NON_RENEWING_PURCHASE", "EXPIRATION", "BILLING_ISSUE", "SUBSCRIBER_ALIAS", "TRANSFER", "TEST"],
            [
                RevenueCatEventType.InitialPurchase, RevenueCatEventType.Renewal, RevenueCatEventType.ProductChange,
                RevenueCatEventType.Cancellation, RevenueCatEventType.Uncancellation, RevenueCatEventType.NonRenewingPurchase,
                RevenueCatEventType.Expiration, RevenueCatEventType.BillingIssue, RevenueCatEventType.SubscriberAlias,
                RevenueCatEventType.Transfer, RevenueCatEventType.Test,
            ]);
    }
}
