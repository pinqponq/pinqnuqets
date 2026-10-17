using Microsoft.Extensions.Logging;

namespace Pinqponq.ErrorHandling.Tests;

internal sealed record CapturedLogRecord(LogLevel Level, string Message, Exception? Exception);

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogRecord> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Records.Add(new CapturedLogRecord(logLevel, formatter(state, exception), exception));
    }
}

internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public List<CapturedLogRecord> Records { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(Records);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class ForwardingLogger(List<CapturedLogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            records.Add(new CapturedLogRecord(logLevel, formatter(state, exception), exception));
        }
    }
}
