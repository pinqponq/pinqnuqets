namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// A subscriber's subscription as the handler reads and writes it. The consuming product maps
/// it to its own storage in <see cref="IRevenueCatSubscriptionStore"/>.
/// </summary>
public sealed class RevenueCatSubscription
{
    /// <summary>The product's own id for the subscriber, as returned by <see cref="IRevenueCatSubscriptionStore.TryResolveSubscriberId"/>.</summary>
    public string SubscriberId { get; set; } = string.Empty;

    /// <summary>The <c>app_user_id</c> RevenueCat knows the subscriber by.</summary>
    public string RevenueCatAppUserId { get; set; } = string.Empty;

    public string EntitlementId { get; set; } = string.Empty;

    public string? ProductId { get; set; }

    public string? Store { get; set; }

    public bool IsActive { get; set; }

    /// <summary>UTC end of the current period; null for lifetime or non-renewing purchases.</summary>
    public DateTime? ExpiresAt { get; set; }

    public bool WillRenew { get; set; }

    public bool IsInGracePeriod { get; set; }

    public string? OriginalTransactionId { get; set; }

    public string? LastEventType { get; set; }

    public DateTime? LastEventAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
