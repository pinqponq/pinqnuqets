using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqloq;

namespace Pinqponq.Observability;

/// <summary>
/// <see cref="ILogService"/> that writes to <c>ILogger</c> and enqueues the same record to Pinqloq.
/// </summary>
public sealed class LogService : ILogService
{
    private const string PathField = "path";
    private const string ExceptionTypeField = "exceptionType";
    private const string ExceptionMessageField = "exceptionMessage";
    private const string StackTraceField = "stackTrace";
    private const string InnerExceptionTypeField = "innerExceptionType";
    private const string InnerExceptionMessageField = "innerExceptionMessage";
    private const string AggregatedExceptionCountField = "aggregatedExceptionCount";

    // Keeps a deep stack trace from inflating the Pinqloq payload.
    private const int MaxStackTraceLength = 4000;

    private readonly ILogger<LogService> _logger;
    private readonly IPinqloqLogger _pinqloqLogger;
    private readonly string _jobsCollectionName;

    /// <summary>Creates the service.</summary>
    public LogService(
        ILogger<LogService> logger,
        IPinqloqLogger pinqloqLogger,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        _logger = logger;
        _pinqloqLogger = pinqloqLogger;
        _jobsCollectionName = observabilityOptions.Value.JobsCollectionName;
    }

    /// <inheritdoc />
    public void Info(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null)
    {
        _logger.LogInformation("{Message} {Data}", message, Serialize(data));
        Enqueue(message, PinqloqLogLevel.Information, data, collectionName, eventName, identifier, path, correlationId);
    }

    /// <inheritdoc />
    public void Warn(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null)
    {
        _logger.LogWarning("{Message} {Data}", message, Serialize(data));
        Enqueue(message, PinqloqLogLevel.Warning, data, collectionName, eventName, identifier, path, correlationId);
    }

    /// <inheritdoc />
    public void Warn(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null)
    {
        _logger.LogWarning(exception, "{Message} {Data}", message, Serialize(data));
        Enqueue(message, PinqloqLogLevel.Warning, data, collectionName, eventName, identifier, path, correlationId, exception);
    }

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null)
    {
        _logger.LogError(exception, "{Message} {Data}", message, Serialize(data));
        Enqueue(message, PinqloqLogLevel.Error, data, collectionName, eventName, identifier, path, correlationId, exception);
    }

    /// <inheritdoc />
    public void Debug(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null)
    {
        _logger.LogDebug("{Message} {Data}", message, Serialize(data));
        Enqueue(message, PinqloqLogLevel.Debug, data, collectionName, eventName, identifier, path, correlationId);
    }

    // An empty identifier stays empty on the entry: the SDK then falls back to the global
    // PinqloqOptions.DeviceIdentifier (background jobs have no caller), so this never fails on it.
    private void Enqueue(
        string message,
        PinqloqLogLevel level,
        object? data,
        string? collectionName,
        string? eventName,
        string? identifier,
        string? path,
        string? correlationId,
        Exception? exception = null)
    {
        var metadata = BuildLogFields(data, exception);
        var detail = BuildLogFields(data, exception);

        string? metadataPath = null;
        if (metadata is not null && metadata.Remove(PathField, out var removedMetadataPath))
        {
            metadataPath = removedMetadataPath;
        }

        detail?.Remove(PathField);

        var resolvedPath = path ?? metadataPath;

        var entry = new PinqloqLogEntry
        {
            Event = eventName ?? message,
            DeviceIdentifier = identifier ?? string.Empty,
            LogLevel = level,
            LogSourceType = PinqloqLogSourceType.Backend,
            CollectionName = collectionName ?? _jobsCollectionName,
            Metadata = metadata,
            Detail = detail,
            Path = resolvedPath,
            CorrelationId = correlationId,
        };

        _pinqloqLogger.Enqueue(entry, onFailed: (_, error) =>
        {
            _logger.LogWarning(
                "pinqloq delivery failed — reason: {Reason}, status: {StatusCode}, message: {Message}",
                error.Reason,
                error.StatusCode,
                error.Message);
        });
    }

    // Pinqloq expects null rather than an empty dictionary when a record has no fields.
    private static Dictionary<string, string>? BuildLogFields(object? data, Exception? exception)
    {
        var logFields = ExtractStringMap(data) ?? new Dictionary<string, string>();

        if (exception is not null)
        {
            AppendExceptionFields(logFields, exception);
        }

        return logFields.Count > 0 ? logFields : null;
    }

    // An AggregateException carries the failures of every parallel branch: the record gets
    // their count and the detail of the first one.
    private static void AppendExceptionFields(Dictionary<string, string> logFields, Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            var flattenedExceptions = aggregateException.Flatten().InnerExceptions;
            logFields[AggregatedExceptionCountField] = flattenedExceptions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var firstException = flattenedExceptions.FirstOrDefault();
            if (firstException is not null)
            {
                AppendSingleExceptionFields(logFields, firstException);
                return;
            }
        }

        AppendSingleExceptionFields(logFields, exception);
    }

    private static void AppendSingleExceptionFields(Dictionary<string, string> logFields, Exception exception)
    {
        var exceptionType = exception.GetType();
        logFields[ExceptionTypeField] = exceptionType.FullName ?? exceptionType.Name;
        logFields[ExceptionMessageField] = exception.Message;

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            logFields[StackTraceField] = TruncateStackTrace(exception.StackTrace);
        }

        var innerException = exception.InnerException;
        if (innerException is null)
        {
            return;
        }

        var innerExceptionType = innerException.GetType();
        logFields[InnerExceptionTypeField] = innerExceptionType.FullName ?? innerExceptionType.Name;
        logFields[InnerExceptionMessageField] = innerException.Message;
    }

    private static string TruncateStackTrace(string stackTrace)
    {
        return stackTrace.Length <= MaxStackTraceLength
            ? stackTrace
            : stackTrace[..MaxStackTraceLength];
    }

    private static Dictionary<string, string>? ExtractStringMap(object? data)
    {
        if (data is not Dictionary<string, object?> dictionary)
        {
            return null;
        }

        var result = new Dictionary<string, string>();

        foreach (var (key, value) in dictionary)
        {
            if (value is not null)
            {
                result[key] = value is string stringValue
                    ? stringValue
                    : Serialize(value) ?? string.Empty;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static string? Serialize(object? data)
    {
        return data is null ? null : LogPayloadSerializer.ToJson(data);
    }
}
