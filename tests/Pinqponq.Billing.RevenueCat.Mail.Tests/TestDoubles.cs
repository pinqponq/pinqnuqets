using Microsoft.Extensions.Logging;
using Pinqponq.Mail;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Messages { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FixedRecipientResolver : IRevenueCatMailRecipientResolver
{
    public RevenueCatMailRecipient? Recipient { get; set; } = new("subscriber@example.com", null);

    public Exception? ExceptionToThrow { get; set; }

    public List<string> ResolvedSubscriberIds { get; } = [];

    public Task<RevenueCatMailRecipient?> Resolve(string subscriberId, CancellationToken cancellationToken = default)
    {
        ResolvedSubscriberIds.Add(subscriberId);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(Recipient);
    }
}

internal sealed class RecordingObserver : IRevenueCatWebhookObserver
{
    public List<RevenueCatWebhookLog> Logs { get; } = [];

    public void Observe(RevenueCatWebhookLog log) => Logs.Add(log);
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}

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
