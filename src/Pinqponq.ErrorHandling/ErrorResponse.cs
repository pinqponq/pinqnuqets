namespace Pinqponq.ErrorHandling;

/// <summary>
/// Standard error response contract. Serialized camelCase, e.g.
/// <c>{ "status": false, "statusCode": 400, "message": "...", "responseCode": "bad_request", "traceId": "..." }</c>.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>Always false for an error.</summary>
    public bool Status { get; init; }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; init; }

    /// <summary>Human-readable message.</summary>
    public string? Message { get; init; }

    /// <summary>Stable machine-readable code (e.g. <c>not_found</c>).</summary>
    public string? ResponseCode { get; init; }

    /// <summary>Correlation/trace id for locating the request in logs.</summary>
    public string? TraceId { get; init; }
}
