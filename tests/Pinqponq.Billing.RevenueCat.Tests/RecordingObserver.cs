namespace Pinqponq.Billing.RevenueCat.Tests;

internal sealed class RecordingObserver : IRevenueCatWebhookObserver
{
    public List<RevenueCatWebhookLog> Logs { get; } = [];

    public void Observe(RevenueCatWebhookLog log) => Logs.Add(log);
}
