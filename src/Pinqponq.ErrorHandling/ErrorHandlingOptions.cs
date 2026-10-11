namespace Pinqponq.ErrorHandling;

/// <summary>
/// Configuration for the global exception middleware.
/// </summary>
public sealed class ErrorHandlingOptions
{
    /// <summary>Header carrying an inbound correlation id. Defaults to <c>X-Correlation-ID</c>.</summary>
    public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";

    /// <summary>
    /// Whether to surface the exception message in the response body. Defaults to false
    /// so internal details are not leaked; a generic message is returned instead.
    /// Ignored when error localization is enabled.
    /// </summary>
    public bool IncludeExceptionMessage { get; set; }

    /// <summary>
    /// Whether the response body carries the <c>traceId</c> field. Defaults to true.
    /// </summary>
    public bool IncludeTraceId { get; set; } = true;

    /// <summary>
    /// Whether every <see cref="OperationCanceledException"/> is answered as a closed client
    /// request (HTTP 499, no body). Defaults to false: only a cancellation of
    /// <c>HttpContext.RequestAborted</c> is, any other one is answered 504 <c>timeout</c>.
    /// </summary>
    public bool TreatAnyCancellationAsClientClosed { get; set; }

    /// <summary>
    /// Optional override mapping an exception to both an HTTP status code and a response
    /// code. Return <see langword="null"/> to fall back to the built-in mapping.
    /// </summary>
    public Func<Exception, ExceptionStatusMapping?>? StatusMappingResolver { get; set; }
}
