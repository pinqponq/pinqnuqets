namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// Receives a structured record for each noteworthy step of the handler, next to its
/// <see cref="Microsoft.Extensions.Logging.ILogger"/> output. Lets a product forward them to
/// its own log pipeline. The default implementation ignores them.
/// </summary>
public interface IRevenueCatWebhookObserver
{
    void Observe(RevenueCatWebhookLog log);
}

/// <summary>One observed handler step. <see cref="EventName"/> is one of <see cref="RevenueCatWebhookLogEvents"/>.</summary>
public sealed record RevenueCatWebhookLog(
    RevenueCatWebhookLogLevel Level,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, object?>? Data);

public enum RevenueCatWebhookLogLevel
{
    Info,
    Warning,
}

/// <summary>Names of the observed handler steps.</summary>
public static class RevenueCatWebhookLogEvents
{
    public const string PayloadInvalid = "webhook_revenuecat_payload_invalid";
    public const string EventDuplicate = "webhook_revenuecat_event_duplicate";
    public const string TestReceived = "webhook_revenuecat_test_received";
    public const string SubscriberAliasReceived = "webhook_revenuecat_subscriber_alias_received";
    public const string EventUnhandled = "webhook_revenuecat_event_unhandled";
    public const string UserUnresolved = "webhook_revenuecat_user_unresolved";
    public const string EntitlementNotPremium = "webhook_revenuecat_entitlement_not_premium";
    public const string CancellationUnknownUser = "webhook_revenuecat_cancellation_unknown_user";
    public const string TransferSkipped = "webhook_revenuecat_transfer_skipped";
    public const string EventSuperseded = "webhook_revenuecat_event_superseded";
    public const string SubscriptionUnknown = "webhook_revenuecat_subscription_unknown";
}

/// <summary>Keys of <see cref="RevenueCatWebhookLog.Data"/>.</summary>
public static class RevenueCatWebhookLogKeys
{
    public const string EventId = "eventId";
    public const string EventType = "eventType";
    public const string AppUserId = "appUserId";
    public const string SubscriberId = "subscriberId";
    public const string Entitlements = "entitlements";
    public const string PremiumEntitlementId = "premiumEntitlementId";
    public const string EventOriginalTransactionId = "eventOriginalTransactionId";
    public const string EventExpiresAt = "eventExpiresAt";
    public const string StoredOriginalTransactionId = "storedOriginalTransactionId";
    public const string StoredExpiresAt = "storedExpiresAt";
}

internal sealed class NullRevenueCatWebhookObserver : IRevenueCatWebhookObserver
{
    public void Observe(RevenueCatWebhookLog log)
    {
    }
}
