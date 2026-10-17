namespace Pinqponq.Contracts;

/// <summary>
/// Response envelope shared by every Pinqponq API. Serialized camelCase, e.g.
/// <c>{ "status": false, "statusCode": 400, "message": "...", "responseCode": "general_error" }</c>.
/// </summary>
public class BaseResponseModel
{
    /// <summary>True for a successful response, false for an error.</summary>
    public bool Status { get; set; }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Human-readable message. Always set on an error: deveng-networking-kmp decodes the error
    /// body into an <c>ErrorResponse(message: String)</c> and shows this text.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>Stable machine-readable code (e.g. <c>general_error</c>).</summary>
    public string? ResponseCode { get; set; }
}
