namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// How the consuming product stores subscriptions and processed events. Changes made through
/// <see cref="SaveSubscription"/> and <see cref="RecordProcessedEvent"/> are collected and
/// written together by one <see cref="Commit"/> call, so an event and its effects are stored
/// atomically.
/// </summary>
public interface IRevenueCatSubscriptionStore
{
    /// <summary>
    /// Maps a RevenueCat <c>app_user_id</c> to the product's subscriber id. False when the id
    /// does not belong to a subscriber (for example an anonymous RevenueCat id).
    /// </summary>
    bool TryResolveSubscriberId(string? appUserId, out string subscriberId);

    /// <summary>True when an event with this id was already recorded.</summary>
    Task<bool> HasProcessedEvent(string eventId, CancellationToken cancellationToken = default);

    /// <summary>Remembers the event so a retried delivery is skipped.</summary>
    void RecordProcessedEvent(string eventId, string eventType, DateTime processedAtUtc);

    Task<RevenueCatSubscription?> FindSubscription(string subscriberId, CancellationToken cancellationToken = default);

    /// <summary>Creates the subscription, or updates it when one already exists for <see cref="RevenueCatSubscription.SubscriberId"/>.</summary>
    void SaveSubscription(RevenueCatSubscription subscription);

    /// <summary>Writes everything collected since the last call.</summary>
    Task Commit(CancellationToken cancellationToken = default);
}
