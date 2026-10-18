using Pinqponq.Observability;

namespace Pinqponq.Push.Fcm.Tests;

internal sealed record LogEntry(string Level, string Message, IReadOnlyDictionary<string, object?>? Data, string? EventName);

internal sealed class RecordingLogService : ILogService
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Info(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Record("Info", message, data, eventName);

    public void Warn(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Record("Warn", message, data, eventName);

    public void Warn(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Record("Warn", message, data, eventName);

    public void Error(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Record("Error", message, data, eventName);

    public void Debug(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null) =>
        Record("Debug", message, data, eventName);

    private void Record(string level, string message, object? data, string? eventName) =>
        _entries.Add(new LogEntry(level, message, data as IReadOnlyDictionary<string, object?>, eventName));
}

internal sealed class RecordingInvalidTokenHandler : IFcmInvalidTokenHandler
{
    private readonly List<IReadOnlyList<string>> _calls = new();

    public IReadOnlyList<IReadOnlyList<string>> Calls => _calls;

    public Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default)
    {
        _calls.Add(invalidTokens);
        return Task.CompletedTask;
    }
}
