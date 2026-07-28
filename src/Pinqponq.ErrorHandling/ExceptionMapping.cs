using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Maps exceptions to an HTTP status code and a stable response code.
/// </summary>
internal static class ExceptionMapping
{
    public static (int StatusCode, string ResponseCode) Map(Exception exception) => exception switch
    {
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "unauthorized"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "not_found"),
        ArgumentException => (StatusCodes.Status400BadRequest, "bad_request"),
        FormatException => (StatusCodes.Status400BadRequest, "bad_request"),
        InvalidOperationException => (StatusCodes.Status400BadRequest, "bad_request"),
        NotImplementedException => (StatusCodes.Status501NotImplemented, "not_implemented"),
        TimeoutException => (StatusCodes.Status504GatewayTimeout, "timeout"),
        _ when exception.GetType().Name.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
            => (StatusCodes.Status404NotFound, "not_found"),
        _ => (StatusCodes.Status500InternalServerError, "internal_error"),
    };

    public static string DefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request was invalid.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "Access is denied.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        StatusCodes.Status422UnprocessableEntity => "The request could not be processed.",
        StatusCodes.Status501NotImplemented => "The requested operation is not implemented.",
        StatusCodes.Status504GatewayTimeout => "The operation timed out.",
        _ when statusCode is >= 400 and < 500 => "The request was invalid.",
        _ => "An unexpected error occurred.",
    };
}
