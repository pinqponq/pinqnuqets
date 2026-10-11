namespace Pinqponq.Billing.RevenueCat.Tests;

/// <summary>Numeric <c>app_user_id</c>s are subscribers, like a product keyed by an integer user id.</summary>
internal sealed class InMemoryRevenueCatStore : IRevenueCatSubscriptionStore
{
    private readonly Dictionary<string, RevenueCatSubscription> _subscriptions = [];
    private readonly List<(string EventId, string EventType)> _processed = [];
    private readonly Dictionary<string, RevenueCatSubscription> _pending = [];
    private readonly List<(string EventId, string EventType)> _pendingProcessed = [];

    public int CommitCount { get; private set; }

    public IReadOnlyList<(string EventId, string EventType)> Processed => _processed;

    public RevenueCatSubscription? Get(string subscriberId) => _subscriptions.GetValueOrDefault(subscriberId);

    public bool TryResolveSubscriberId(string? appUserId, out string subscriberId)
    {
        subscriberId = string.Empty;
        if (!int.TryParse(appUserId, out var number))
        {
            return false;
        }

        subscriberId = number.ToString();
        return true;
    }

    public Task<bool> HasProcessedEvent(string eventId, CancellationToken cancellationToken = default)
        => Task.FromResult(_processed.Any(entry => entry.EventId == eventId));

    public void RecordProcessedEvent(string eventId, string eventType, DateTime processedAtUtc)
        => _pendingProcessed.Add((eventId, eventType));

    public Task<RevenueCatSubscription?> FindSubscription(string subscriberId, CancellationToken cancellationToken = default)
        => Task.FromResult(_pending.GetValueOrDefault(subscriberId) ?? _subscriptions.GetValueOrDefault(subscriberId));

    public void SaveSubscription(RevenueCatSubscription subscription)
        => _pending[subscription.SubscriberId] = subscription;

    public Task Commit(CancellationToken cancellationToken = default)
    {
        foreach (var (subscriberId, subscription) in _pending)
        {
            _subscriptions[subscriberId] = subscription;
        }

        _processed.AddRange(_pendingProcessed);
        _pending.Clear();
        _pendingProcessed.Clear();
        CommitCount++;
        return Task.CompletedTask;
    }
}
