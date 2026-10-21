using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pinqponq.Hosting.Periodic.Tests;

internal sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _logs = new();

    public IReadOnlyList<CapturedLog> Logs => _logs.ToList();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => logs.Enqueue(new CapturedLog(logLevel, formatter(state, exception), exception));
    }
}
