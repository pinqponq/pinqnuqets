using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pinqponq.Playground.Diagnostics;

/// <summary>
/// <see cref="ILoggerProvider"/> that records every entry into a shared <see cref="LogSink"/>.
/// </summary>
/// <remarks>
/// One instance per logger factory. Each scenario run builds its own service provider —
/// and therefore its own factory — so the scope provider must not be shared; the sink is.
/// A run's provider is constructed with its <paramref name="runId"/>, which stamps every
/// entry structurally rather than relying on ambient state that may not flow into broker
/// dispatch threads. The application-wide provider passes null and falls back to
/// <see cref="ScenarioRunContext"/>, which is how logs raised inside the error-handling
/// sandbox request get attributed to the run that triggered them.
/// </remarks>
public sealed class CapturingLoggerProvider(LogSink sink, string? runId = null)
    : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentDictionary<string, CapturingLogger> _loggers = new(StringComparer.Ordinal);
    private IExternalScopeProvider? _scopeProvider;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new CapturingLogger(name, sink, runId, () => _scopeProvider));

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    /// <inheritdoc />
    public void Dispose() => _loggers.Clear();

    private sealed class CapturingLogger(
        string category,
        LogSink sink,
        string? runId,
        Func<IExternalScopeProvider?> scopeProviderAccessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            scopeProviderAccessor()?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            var (template, values) = Flatten(state);
            var scopes = CollectScopes();
            var timestamp = DateTimeOffset.UtcNow;
            var effectiveRunId = runId ?? ScenarioRunContext.RunId;
            var message = formatter(state, exception);

            sink.Write(id => new LogRecord
            {
                Id = id,
                Timestamp = timestamp,
                Level = logLevel.ToString(),
                Category = category,
                EventId = new LogEventIdInfo(eventId.Id, eventId.Name),
                Message = message,
                MessageTemplate = template,
                State = values,
                Scopes = scopes,
                RunId = effectiveRunId,
                Exception = exception is null ? null : LogExceptionInfo.From(exception),
            });
        }

        private IReadOnlyList<IReadOnlyDictionary<string, object?>> CollectScopes()
        {
            var provider = scopeProviderAccessor();
            if (provider is null)
            {
                return [];
            }

            var scopes = new List<IReadOnlyDictionary<string, object?>>();
            provider.ForEachScope(
                (scope, list) =>
                {
                    var (_, values) = Flatten(scope);
                    if (values.Count > 0)
                    {
                        list.Add(values);
                    }
                    else if (scope is not null)
                    {
                        list.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["Scope"] = LogRecord.NormalizeValue(scope),
                        });
                    }
                },
                scopes);

            return scopes;
        }

        /// <summary>
        /// Splits a logging state into its message template and its named values. The
        /// template lives under the well-known <c>{OriginalFormat}</c> key.
        /// </summary>
        private static (string? Template, IReadOnlyDictionary<string, object?> Values) Flatten<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                return (null, new Dictionary<string, object?>(StringComparer.Ordinal));
            }

            string? template = null;
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    template = pair.Value as string;
                    continue;
                }

                values[pair.Key] = LogRecord.NormalizeValue(pair.Value);
            }

            return (template, values);
        }
    }
}
