using Microsoft.Extensions.Logging;
using Pinqloq;

namespace Pinqponq.Observability.Tests;

internal sealed class CapturingPinqloqLogger : IPinqloqLogger
{
    public List<PinqloqLogEntry> Entries { get; } = [];

    public PinqloqLogError? FailureToReport { get; set; }

    public bool Enqueue(PinqloqLogEntry entry, Action<PinqloqLogEntry>? onSent = null, Action<PinqloqLogEntry, PinqloqLogError>? onFailed = null)
    {
        Entries.Add(entry);
        if (FailureToReport is not null)
        {
            onFailed?.Invoke(entry, FailureToReport);
        }

        return true;
    }

    public int Enqueue(IEnumerable<PinqloqLogEntry> entries, Action<PinqloqLogEntry>? onSent = null, Action<PinqloqLogEntry, PinqloqLogError>? onFailed = null) =>
        entries.Count(entry => Enqueue(entry, onSent, onFailed));
}

internal sealed record LogServiceCall(string Level, string Message, Exception? Exception, object? Data, string? CollectionName, string? EventName, string? Identifier, string? CorrelationId)
{
    public Dictionary<string, object?> Fields => Data as Dictionary<string, object?> ?? [];
}

internal sealed class CapturingLogService : ILogService
{
    public List<LogServiceCall> Calls { get; } = [];

    public void Info(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Calls.Add(new LogServiceCall(nameof(Info), message, null, data, collectionName, eventName, identifier, correlationId));

    public void Warn(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Calls.Add(new LogServiceCall(nameof(Warn), message, null, data, collectionName, eventName, identifier, correlationId));

    public void Warn(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Calls.Add(new LogServiceCall(nameof(Warn), message, exception, data, collectionName, eventName, identifier, correlationId));

    public void Error(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Calls.Add(new LogServiceCall(nameof(Error), message, exception, data, collectionName, eventName, identifier, correlationId));

    public void Debug(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Calls.Add(new LogServiceCall(nameof(Debug), message, null, data, collectionName, eventName, identifier, correlationId));
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Records.Add((logLevel, formatter(state, exception), exception));
}
