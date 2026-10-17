namespace Pinqponq.ErrorHandling;

/// <summary>
/// Log event names the middleware puts on <c>Pinqponq.Contracts.IRequestErrorFeature</c>.
/// </summary>
public static class ErrorHandlingEvents
{
    /// <summary>The client closed the request.</summary>
    public const string RequestCancelledByClient = "request_cancelled_by_client";

    private const string ExceptionEventPrefix = "exception_";

    /// <summary>Event name of a handled exception: <c>exception_&lt;TypeName&gt;</c>.</summary>
    public static string BuildExceptionEventName(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return $"{ExceptionEventPrefix}{exception.GetType().Name}";
    }
}
