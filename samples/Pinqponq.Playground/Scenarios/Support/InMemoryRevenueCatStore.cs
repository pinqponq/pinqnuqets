using Pinqponq.Billing.RevenueCat;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>
/// The subscription storage the package deliberately does not ship. Kept per run so the console
/// can show what the handler wrote. Numeric <c>app_user_id</c>s are subscribers.
/// </summary>
public sealed class InMemoryRevenueCatStore : IRevenueCatSubscriptionStore
{
    private readonly Dictionary<string, RevenueCatSubscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _processedEventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RevenueCatSubscription> _pendingSubscriptions = new(StringComparer.Ordinal);
    private readonly List<string> _pendingEventIds = [];

    /// <summary>Everything currently stored, for display.</summary>
    public IReadOnlyCollection<RevenueCatSubscription> All => [.. _subscriptions.Values];

    /// <summary>Ids of the events recorded as processed.</summary>
    public IReadOnlyCollection<string> ProcessedEventIds => [.. _processedEventIds];

    /// <inheritdoc />
    public bool TryResolveSubscriberId(string? appUserId, out string subscriberId)
    {
        subscriberId = int.TryParse(appUserId, out var number) ? number.ToString() : string.Empty;
        return subscriberId.Length > 0;
    }

    /// <inheritdoc />
    public Task<bool> HasProcessedEvent(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processedEventIds.Contains(eventId));

    /// <inheritdoc />
    public void RecordProcessedEvent(string eventId, string eventType, DateTime processedAtUtc) =>
        _pendingEventIds.Add(eventId);

    /// <inheritdoc />
    public Task<RevenueCatSubscription?> FindSubscription(string subscriberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pendingSubscriptions.GetValueOrDefault(subscriberId) ?? _subscriptions.GetValueOrDefault(subscriberId));

    /// <inheritdoc />
    public void SaveSubscription(RevenueCatSubscription subscription) =>
        _pendingSubscriptions[subscription.SubscriberId] = subscription;

    /// <inheritdoc />
    public Task Commit(CancellationToken cancellationToken = default)
    {
        foreach (var (subscriberId, subscription) in _pendingSubscriptions)
        {
            _subscriptions[subscriberId] = subscription;
        }

        _processedEventIds.UnionWith(_pendingEventIds);
        _pendingSubscriptions.Clear();
        _pendingEventIds.Clear();
        return Task.CompletedTask;
    }
}
