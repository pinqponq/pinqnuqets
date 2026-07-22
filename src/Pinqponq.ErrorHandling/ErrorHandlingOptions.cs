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
    /// </summary>
    public bool IncludeExceptionMessage { get; set; }

    /// <summary>
    /// Optional override mapping an exception to an HTTP status code. Return null to fall
    /// back to the built-in mapping.
    /// </summary>
    public Func<Exception, int?>? StatusCodeResolver { get; set; }
}
