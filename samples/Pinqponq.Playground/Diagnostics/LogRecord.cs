using System.Globalization;

namespace Pinqponq.Playground.Diagnostics;

/// <summary>
/// One captured <see cref="Microsoft.Extensions.Logging.ILogger"/> entry, kept in the
/// shape the console needs: the formatted message *and* the raw message template and
/// state, so field names such as <c>TraceId</c> or <c>CorrelationId</c> can be inspected.
/// </summary>
public sealed record LogRecord
{
    /// <summary>Monotonic id, used by the client to resume a stream.</summary>
    public required long Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Log level name (<c>Trace</c>…<c>Critical</c>).</summary>
    public required string Level { get; init; }

    /// <summary>Logger category, usually the fully qualified type name.</summary>
    public required string Category { get; init; }

    public required LogEventIdInfo EventId { get; init; }

    /// <summary>The formatted message as a sink would write it.</summary>
    public required string Message { get; init; }

    /// <summary>The original message template, when the state carried one.</summary>
    public string? MessageTemplate { get; init; }

    /// <summary>Structured state values, template placeholder name to value.</summary>
    public IReadOnlyDictionary<string, object?> State { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Active logging scopes, outermost first.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes { get; init; } = [];

    /// <summary>Scenario run this entry belongs to, when it was emitted during a run.</summary>
    public string? RunId { get; init; }

    public LogExceptionInfo? Exception { get; init; }

    /// <summary>Normalises a state value so it serialises to sensible JSON.</summary>
    internal static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        string or bool or int or long or short or byte or sbyte
            or uint or ulong or ushort or float or double or decimal => value,
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString(null, CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        Enum e => e.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}

/// <summary>Serialisable form of <see cref="Microsoft.Extensions.Logging.EventId"/>.</summary>
public sealed record LogEventIdInfo(int Id, string? Name);

/// <summary>Serialisable exception detail, including the inner exception chain.</summary>
public sealed record LogExceptionInfo(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyList<LogExceptionInfo> Inner)
{
    /// <summary>Projects an exception (and its inner chain) into the serialisable form.</summary>
    public static LogExceptionInfo From(Exception exception)
    {
        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions.Select(From).ToArray()
            : exception.InnerException is { } single
                ? [From(single)]
                : Array.Empty<LogExceptionInfo>();

        return new LogExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            inner);
    }
}
