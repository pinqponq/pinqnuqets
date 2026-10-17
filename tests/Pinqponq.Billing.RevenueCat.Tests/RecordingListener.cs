namespace Pinqponq.Billing.RevenueCat.Tests;

internal sealed class RecordingListener : IRevenueCatEventListener
{
    public List<RevenueCatEventContext> Contexts { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public Action? OnCall { get; set; }

    public Task OnEventProcessed(RevenueCatEventContext context, CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        CancellationTokens.Add(cancellationToken);
        OnCall?.Invoke();
        return Task.CompletedTask;
    }
}
