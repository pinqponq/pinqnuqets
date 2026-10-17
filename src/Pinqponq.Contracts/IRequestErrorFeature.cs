namespace Pinqponq.Contracts;

/// <summary>
/// HTTP feature that describes how a failed request was answered. Pinqponq.ErrorHandling
/// sets it on <c>HttpContext.Features</c>; Pinqponq.Observability reads it so the request's
/// single log record carries the error instead of a second, separate record.
/// </summary>
public interface IRequestErrorFeature
{
    /// <summary>Log event name, e.g. <c>exception_KeyNotFoundException</c>.</summary>
    string EventName { get; }

    /// <summary>Structured error detail (status code, response code, message, …).</summary>
    IReadOnlyDictionary<string, object?> Detail { get; }
}
