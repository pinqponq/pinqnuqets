namespace Pinqponq.ErrorHandling;

/// <summary>
/// HTTP status and stable response code produced for an exception.
/// </summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="ResponseCode">Stable machine-readable response code.</param>
public readonly record struct ExceptionStatusMapping(int StatusCode, string ResponseCode);
