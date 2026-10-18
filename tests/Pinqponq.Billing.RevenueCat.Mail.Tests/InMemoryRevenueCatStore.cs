namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

internal sealed class InMemoryRevenueCatStore : IRevenueCatSubscriptionStore
{
    private readonly Dictionary<string, RevenueCatSubscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _processedEventIds = new(StringComparer.Ordinal);

    public bool TryResolveSubscriberId(string? appUserId, out string subscriberId)
    {
        subscriberId = int.TryParse(appUserId, out var number) ? number.ToString() : string.Empty;
        return subscriberId.Length > 0;
    }

    public Task<bool> HasProcessedEvent(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processedEventIds.Contains(eventId));

    public void RecordProcessedEvent(string eventId, string eventType, DateTime processedAtUtc) =>
        _processedEventIds.Add(eventId);

    public Task<RevenueCatSubscription?> FindSubscription(string subscriberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_subscriptions.GetValueOrDefault(subscriberId));

    public void SaveSubscription(RevenueCatSubscription subscription) =>
        _subscriptions[subscription.SubscriberId] = subscription;

    public Task Commit(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
