using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// Default <see cref="IRevenueCatEventHandler"/>. Persistence goes through
/// <see cref="IRevenueCatSubscriptionStore"/>; everything RevenueCat-specific lives here.
/// </summary>
public sealed class RevenueCatEventHandler(
    IRevenueCatSubscriptionStore store,
    IOptions<RevenueCatOptions> options,
    IRevenueCatWebhookObserver observer,
    IEnumerable<IRevenueCatEventListener> listeners,
    ILogger<RevenueCatEventHandler> logger) : IRevenueCatEventHandler
{
    private readonly RevenueCatOptions _options = options.Value;

    public async Task<bool> Handle(RevenueCatWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var revenueCatEvent = payload.Event;
        if (revenueCatEvent is null
            || string.IsNullOrWhiteSpace(revenueCatEvent.Id)
            || string.IsNullOrWhiteSpace(revenueCatEvent.Type))
        {
            logger.LogWarning("RevenueCat webhook payload missing event/id/type — ignoring.");
            Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatWebhookLogEvents.PayloadInvalid,
                "RevenueCat webhook payload missing event/id/type — ignoring.");
            return false;
        }

        if (await store.HasProcessedEvent(revenueCatEvent.Id, cancellationToken))
        {
            logger.LogInformation("RevenueCat event {EventId} ({EventType}) already processed — skipping.",
                revenueCatEvent.Id, revenueCatEvent.Type);
            Observe(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.EventDuplicate,
                "RevenueCat event already processed — skipping.",
                (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
                (RevenueCatWebhookLogKeys.EventType, revenueCatEvent.Type));
            return true;
        }

        if (revenueCatEvent.Type == RevenueCatEventType.Test)
        {
            logger.LogInformation("RevenueCat TEST event {EventId} received — recording and returning OK.", revenueCatEvent.Id);
            Observe(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.TestReceived,
                "RevenueCat TEST event received — recording and returning OK.",
                (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id));
            MarkProcessed(revenueCatEvent.Id, revenueCatEvent.Type);
            await store.Commit(cancellationToken);
            return true;
        }

        switch (revenueCatEvent.Type)
        {
            case RevenueCatEventType.InitialPurchase:
            case RevenueCatEventType.Renewal:
            case RevenueCatEventType.ProductChange:
            case RevenueCatEventType.Uncancellation:
            case RevenueCatEventType.NonRenewingPurchase:
                await HandleActivation(revenueCatEvent, cancellationToken);
                break;

            case RevenueCatEventType.Cancellation:
                await HandleCancellation(revenueCatEvent, cancellationToken);
                break;

            case RevenueCatEventType.Expiration:
                await HandleExpiration(revenueCatEvent, cancellationToken);
                break;

            case RevenueCatEventType.BillingIssue:
                await HandleBillingIssue(revenueCatEvent, cancellationToken);
                break;

            case RevenueCatEventType.Transfer:
                await HandleTransfer(revenueCatEvent, cancellationToken);
                break;

            case RevenueCatEventType.SubscriberAlias:
                logger.LogInformation("SUBSCRIBER_ALIAS event {EventId} received for app_user_id={AppUserId}; no state change required.",
                    revenueCatEvent.Id, revenueCatEvent.AppUserId);
                Observe(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.SubscriberAliasReceived,
                    "RevenueCat SUBSCRIBER_ALIAS event received — no state change required.",
                    (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
                    (RevenueCatWebhookLogKeys.AppUserId, revenueCatEvent.AppUserId));
                break;

            default:
                logger.LogWarning("Unhandled RevenueCat event type {EventType} (id={EventId}).",
                    revenueCatEvent.Type, revenueCatEvent.Id);
                Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatWebhookLogEvents.EventUnhandled,
                    "Unhandled RevenueCat event type.",
                    (RevenueCatWebhookLogKeys.EventType, revenueCatEvent.Type),
                    (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id));
                break;
        }

        MarkProcessed(revenueCatEvent.Id, revenueCatEvent.Type);
        await store.Commit(cancellationToken);
        await NotifyListeners(revenueCatEvent, cancellationToken);
        return true;
    }

    private async Task NotifyListeners(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        var context = new RevenueCatEventContext(
            revenueCatEvent,
            TryResolveSubscriberId(revenueCatEvent, out var subscriberId) ? subscriberId : null,
            IsPremiumEntitlement(revenueCatEvent));
        foreach (var listener in listeners)
        {
            await listener.OnEventProcessed(context, cancellationToken);
        }
    }

    private async Task HandleActivation(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        if (!TryResolveSubscriberIdOrLog(revenueCatEvent, out var subscriberId))
        {
            return;
        }

        var isPremium = IsPremiumEntitlement(revenueCatEvent);
        if (!isPremium)
        {
            var entitlements = string.Join(",", revenueCatEvent.EntitlementIds ?? []);
            logger.LogInformation("Event {EventId} entitlements {Entitlements} do not include premium entitlement {Premium}; recording but not activating.",
                revenueCatEvent.Id, entitlements, _options.PremiumEntitlementId);
            Observe(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.EntitlementNotPremium,
                "Event entitlements do not include premium — recording but not activating.",
                (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
                (RevenueCatWebhookLogKeys.Entitlements, entitlements),
                (RevenueCatWebhookLogKeys.PremiumEntitlementId, _options.PremiumEntitlementId));
        }

        var subscription = await GetOrCreate(subscriberId, revenueCatEvent, cancellationToken);

        subscription.IsActive = isPremium;
        subscription.WillRenew = true;
        subscription.IsInGracePeriod = false;
        subscription.ProductId = revenueCatEvent.NewProductId ?? revenueCatEvent.ProductId ?? subscription.ProductId;
        subscription.Store = revenueCatEvent.Store ?? subscription.Store;
        subscription.ExpiresAt = FromUnixMs(revenueCatEvent.ExpirationAtMs) ?? subscription.ExpiresAt;
        subscription.OriginalTransactionId = revenueCatEvent.OriginalTransactionId ?? subscription.OriginalTransactionId;

        UpdateAudit(subscription, revenueCatEvent);
        store.SaveSubscription(subscription);
    }

    private async Task HandleCancellation(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        if (!TryResolveSubscriberIdOrLog(revenueCatEvent, out var subscriberId))
        {
            return;
        }

        var subscription = await store.FindSubscription(subscriberId, cancellationToken);
        if (subscription is null)
        {
            LogUnknownSubscriber(revenueCatEvent, subscriberId, RevenueCatWebhookLogEvents.CancellationUnknownUser);
            return;
        }

        if (IsSupersededEvent(subscription, revenueCatEvent))
        {
            return;
        }

        // After a cancellation the subscriber keeps the remaining period: IsActive stays true until ExpiresAt.
        subscription.WillRenew = false;
        UpdateAudit(subscription, revenueCatEvent);
        store.SaveSubscription(subscription);
    }

    private async Task HandleExpiration(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        if (!TryResolveSubscriberIdOrLog(revenueCatEvent, out var subscriberId))
        {
            return;
        }

        var subscription = await store.FindSubscription(subscriberId, cancellationToken);
        if (subscription is null)
        {
            LogUnknownSubscriber(revenueCatEvent, subscriberId, RevenueCatWebhookLogEvents.SubscriptionUnknown);
            return;
        }

        if (IsSupersededEvent(subscription, revenueCatEvent))
        {
            return;
        }

        subscription.IsActive = false;
        subscription.WillRenew = false;
        subscription.IsInGracePeriod = false;
        UpdateAudit(subscription, revenueCatEvent);
        store.SaveSubscription(subscription);
    }

    private async Task HandleBillingIssue(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        if (!TryResolveSubscriberIdOrLog(revenueCatEvent, out var subscriberId))
        {
            return;
        }

        var subscription = await store.FindSubscription(subscriberId, cancellationToken);
        if (subscription is null)
        {
            LogUnknownSubscriber(revenueCatEvent, subscriberId, RevenueCatWebhookLogEvents.SubscriptionUnknown);
            return;
        }

        if (IsSupersededEvent(subscription, revenueCatEvent))
        {
            return;
        }

        // On BILLING_ISSUE the subscriber is in the grace period and still counts as premium;
        // an EXPIRATION event follows when the payment is not fixed.
        subscription.IsInGracePeriod = true;
        UpdateAudit(subscription, revenueCatEvent);
        store.SaveSubscription(subscription);
    }

    private async Task HandleTransfer(RevenueCatEvent revenueCatEvent, CancellationToken cancellationToken)
    {
        // TRANSFER moves a subscription between two subscribers sharing a store account:
        // deactivate everyone in transferred_from, activate everyone in transferred_to.
        if (revenueCatEvent.TransferredFrom is { Count: > 0 })
        {
            foreach (var oldAppUserId in revenueCatEvent.TransferredFrom)
            {
                if (!store.TryResolveSubscriberId(oldAppUserId, out var oldSubscriberId))
                {
                    logger.LogWarning("TRANSFER from unresolvable app_user_id '{AppUserId}' (event {EventId}) — skipping deactivation.",
                        oldAppUserId, revenueCatEvent.Id);
                    Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatWebhookLogEvents.TransferSkipped,
                        "RevenueCat TRANSFER from unresolvable app_user_id — skipping deactivation.",
                        (RevenueCatWebhookLogKeys.AppUserId, oldAppUserId),
                        (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id));
                    continue;
                }

                var oldSubscription = await store.FindSubscription(oldSubscriberId, cancellationToken);
                if (oldSubscription is null)
                {
                    continue;
                }

                oldSubscription.IsActive = false;
                oldSubscription.WillRenew = false;
                UpdateAudit(oldSubscription, revenueCatEvent);
                store.SaveSubscription(oldSubscription);
            }
        }

        var toList = revenueCatEvent.TransferredTo
            ?? (string.IsNullOrWhiteSpace(revenueCatEvent.AppUserId) ? [] : new List<string> { revenueCatEvent.AppUserId });

        foreach (var newAppUserId in toList)
        {
            if (!store.TryResolveSubscriberId(newAppUserId, out var newSubscriberId))
            {
                logger.LogWarning("TRANSFER to unresolvable app_user_id '{AppUserId}' (event {EventId}) — skipping activation.",
                    newAppUserId, revenueCatEvent.Id);
                Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatWebhookLogEvents.TransferSkipped,
                    "RevenueCat TRANSFER to unresolvable app_user_id — skipping activation.",
                    (RevenueCatWebhookLogKeys.AppUserId, newAppUserId),
                    (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id));
                continue;
            }

            var newSubscription = await GetOrCreate(newSubscriberId, revenueCatEvent, cancellationToken);
            newSubscription.IsActive = IsPremiumEntitlement(revenueCatEvent);
            newSubscription.WillRenew = true;
            newSubscription.ExpiresAt = FromUnixMs(revenueCatEvent.ExpirationAtMs) ?? newSubscription.ExpiresAt;
            newSubscription.ProductId = revenueCatEvent.ProductId ?? newSubscription.ProductId;
            newSubscription.Store = revenueCatEvent.Store ?? newSubscription.Store;
            newSubscription.OriginalTransactionId = revenueCatEvent.OriginalTransactionId ?? newSubscription.OriginalTransactionId;
            UpdateAudit(newSubscription, revenueCatEvent);
            store.SaveSubscription(newSubscription);
        }
    }

    private async Task<RevenueCatSubscription> GetOrCreate(
        string subscriberId,
        RevenueCatEvent revenueCatEvent,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindSubscription(subscriberId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTime.UtcNow;
        return new RevenueCatSubscription
        {
            SubscriberId = subscriberId,
            RevenueCatAppUserId = revenueCatEvent.AppUserId ?? subscriberId,
            EntitlementId = _options.PremiumEntitlementId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private void MarkProcessed(string eventId, string eventType)
        => store.RecordProcessedEvent(eventId, eventType, DateTime.UtcNow);

    private static void UpdateAudit(RevenueCatSubscription subscription, RevenueCatEvent revenueCatEvent)
    {
        subscription.LastEventType = revenueCatEvent.Type;
        subscription.LastEventAt = FromUnixMs(revenueCatEvent.EventTimestampMs) ?? DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether the event belongs to an older subscription period that the stored one has replaced.
    /// When a subscriber cancels and resubscribes, RevenueCat sends a CANCELLATION/EXPIRATION for
    /// the OLD transaction right after the new INITIAL_PURCHASE. Applying it would wrongly drop
    /// the fresh premium.
    /// </summary>
    private bool IsSupersededEvent(RevenueCatSubscription subscription, RevenueCatEvent revenueCatEvent)
    {
        var isForOtherTransaction = !string.IsNullOrWhiteSpace(subscription.OriginalTransactionId)
            && !string.IsNullOrWhiteSpace(revenueCatEvent.OriginalTransactionId)
            && !string.Equals(subscription.OriginalTransactionId, revenueCatEvent.OriginalTransactionId, StringComparison.Ordinal);

        var eventExpiresAt = FromUnixMs(revenueCatEvent.ExpirationAtMs);
        var isBehindStoredExpiry = eventExpiresAt.HasValue
            && subscription.ExpiresAt.HasValue
            && eventExpiresAt < subscription.ExpiresAt;

        if (!isForOtherTransaction && !isBehindStoredExpiry)
        {
            return false;
        }

        logger.LogInformation(
            "RevenueCat {EventType} event {EventId} superseded for subscriber {SubscriberId} — event txn={EventTransactionId} expiresAt={EventExpiresAt}, stored txn={StoredTransactionId} expiresAt={StoredExpiresAt}. Skipping.",
            revenueCatEvent.Type, revenueCatEvent.Id, subscription.SubscriberId,
            revenueCatEvent.OriginalTransactionId, eventExpiresAt,
            subscription.OriginalTransactionId, subscription.ExpiresAt);
        Observe(RevenueCatWebhookLogLevel.Info, RevenueCatWebhookLogEvents.EventSuperseded,
            "RevenueCat event superseded by a newer subscription period — skipping.",
            (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
            (RevenueCatWebhookLogKeys.EventType, revenueCatEvent.Type),
            (RevenueCatWebhookLogKeys.SubscriberId, subscription.SubscriberId),
            (RevenueCatWebhookLogKeys.EventOriginalTransactionId, revenueCatEvent.OriginalTransactionId),
            (RevenueCatWebhookLogKeys.EventExpiresAt, eventExpiresAt),
            (RevenueCatWebhookLogKeys.StoredOriginalTransactionId, subscription.OriginalTransactionId),
            (RevenueCatWebhookLogKeys.StoredExpiresAt, subscription.ExpiresAt));
        return true;
    }

    private bool IsPremiumEntitlement(RevenueCatEvent revenueCatEvent)
        => revenueCatEvent.EntitlementIds is { Count: > 0 }
            && revenueCatEvent.EntitlementIds.Contains(_options.PremiumEntitlementId);

    private bool TryResolveSubscriberIdOrLog(RevenueCatEvent revenueCatEvent, out string subscriberId)
    {
        if (TryResolveSubscriberId(revenueCatEvent, out subscriberId))
        {
            return true;
        }

        logger.LogWarning("Cannot resolve subscriber from app_user_id '{AppUserId}' or aliases for event {EventId} ({EventType}).",
            revenueCatEvent.AppUserId, revenueCatEvent.Id, revenueCatEvent.Type);
        Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatWebhookLogEvents.UserUnresolved,
            "Cannot resolve subscriber from app_user_id or aliases.",
            (RevenueCatWebhookLogKeys.AppUserId, revenueCatEvent.AppUserId),
            (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
            (RevenueCatWebhookLogKeys.EventType, revenueCatEvent.Type));
        return false;
    }

    private void LogUnknownSubscriber(RevenueCatEvent revenueCatEvent, string subscriberId, string eventName)
    {
        logger.LogWarning("{EventType} received for unknown subscriber {SubscriberId} (event {EventId}).",
            revenueCatEvent.Type, subscriberId, revenueCatEvent.Id);
        Observe(RevenueCatWebhookLogLevel.Warning, eventName,
            "RevenueCat event received for unknown subscriber.",
            (RevenueCatWebhookLogKeys.SubscriberId, subscriberId),
            (RevenueCatWebhookLogKeys.EventId, revenueCatEvent.Id),
            (RevenueCatWebhookLogKeys.EventType, revenueCatEvent.Type));
    }

    private bool TryResolveSubscriberId(RevenueCatEvent revenueCatEvent, out string subscriberId)
    {
        if (store.TryResolveSubscriberId(revenueCatEvent.AppUserId, out subscriberId))
        {
            return true;
        }

        if (revenueCatEvent.Aliases is { Count: > 0 })
        {
            foreach (var alias in revenueCatEvent.Aliases)
            {
                if (store.TryResolveSubscriberId(alias, out subscriberId))
                {
                    return true;
                }
            }
        }

        subscriberId = string.Empty;
        return false;
    }

    private void Observe(
        RevenueCatWebhookLogLevel level,
        string eventName,
        string message,
        params (string Key, object? Value)[] data)
    {
        var dictionary = data.Length == 0
            ? null
            : data.ToDictionary(entry => entry.Key, entry => entry.Value);
        observer.Observe(new RevenueCatWebhookLog(level, eventName, message, dictionary));
    }

    private static DateTime? FromUnixMs(long? unixMs)
        => unixMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value).UtcDateTime : null;
}
