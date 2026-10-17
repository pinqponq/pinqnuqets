using System.Text.Json.Serialization;

namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// Root of a RevenueCat webhook body.
/// See https://www.revenuecat.com/docs/integrations/webhooks/event-types-and-fields.
/// </summary>
public sealed class RevenueCatWebhookPayload
{
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("event")]
    public RevenueCatEvent? Event { get; set; }
}

/// <summary>
/// The event object. One type covers every event type, so all fields are nullable; which
/// ones are filled depends on <see cref="Type"/>.
/// </summary>
public sealed class RevenueCatEvent
{
    /// <summary>Unique event id, used for idempotency.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>One of the <see cref="RevenueCatEventType"/> values.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("event_timestamp_ms")]
    public long? EventTimestampMs { get; set; }

    /// <summary>The <c>app_user_id</c> the client logged in to RevenueCat with.</summary>
    [JsonPropertyName("app_user_id")]
    public string? AppUserId { get; set; }

    /// <summary>The <c>app_user_id</c> of the original purchase; differs after a transfer.</summary>
    [JsonPropertyName("original_app_user_id")]
    public string? OriginalAppUserId { get; set; }

    /// <summary>Every <c>app_user_id</c> attached to the same RevenueCat subscriber.</summary>
    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    /// <summary>Entitlements the event affects.</summary>
    [JsonPropertyName("entitlement_ids")]
    public List<string>? EntitlementIds { get; set; }

    /// <summary><c>APP_STORE</c>, <c>PLAY_STORE</c>, <c>STRIPE</c> and so on.</summary>
    [JsonPropertyName("store")]
    public string? Store { get; set; }

    [JsonPropertyName("purchased_at_ms")]
    public long? PurchasedAtMs { get; set; }

    [JsonPropertyName("expiration_at_ms")]
    public long? ExpirationAtMs { get; set; }

    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("original_transaction_id")]
    public string? OriginalTransactionId { get; set; }

    /// <summary>Why a <c>CANCELLATION</c> happened (<c>UNSUBSCRIBE</c>, <c>BILLING_ERROR</c>, ...).</summary>
    [JsonPropertyName("cancel_reason")]
    public string? CancelReason { get; set; }

    /// <summary>End of the grace period on a <c>BILLING_ISSUE</c>.</summary>
    [JsonPropertyName("grace_period_expiration_at_ms")]
    public long? GracePeriodExpirationAtMs { get; set; }

    /// <summary>On a <c>TRANSFER</c>, the <c>app_user_id</c>s the subscription moved away from.</summary>
    [JsonPropertyName("transferred_from")]
    public List<string>? TransferredFrom { get; set; }

    /// <summary>On a <c>TRANSFER</c>, the <c>app_user_id</c>s the subscription moved to.</summary>
    [JsonPropertyName("transferred_to")]
    public List<string>? TransferredTo { get; set; }

    /// <summary>On a <c>PRODUCT_CHANGE</c>, the product the subscriber moved to.</summary>
    [JsonPropertyName("new_product_id")]
    public string? NewProductId { get; set; }
}
