namespace Pinqponq.Observability;

/// <summary>
/// Writes a log record to both <c>ILogger</c> and Pinqloq. <c>data</c> is a
/// <c>Dictionary&lt;string, object?&gt;</c> whose entries become the record's metadata and
/// detail; an exception adds its type, message, trimmed stack trace and inner exception.
/// </summary>
public interface ILogService
{
    /// <summary>Information record.</summary>
    void Info(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null);

    /// <summary>Warning record.</summary>
    void Warn(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null);

    /// <summary>Warning record with an exception.</summary>
    void Warn(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null);

    /// <summary>Error record.</summary>
    void Error(string message, Exception? exception = null, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null);

    /// <summary>Debug record.</summary>
    void Debug(string message, object? data = null, string? collectionName = null, string? eventName = null, string? identifier = null, string? path = null, string? correlationId = null);
}
