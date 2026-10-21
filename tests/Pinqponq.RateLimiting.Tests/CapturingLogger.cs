using Microsoft.Extensions.Logging;

namespace Pinqponq.RateLimiting.Tests;

internal sealed record CapturedLogRecord(LogLevel Level, string Message);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<CapturedLogRecord> Records { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Records);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<CapturedLogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (records)
            {
                records.Add(new CapturedLogRecord(logLevel, formatter(state, exception)));
            }
        }
    }
}
