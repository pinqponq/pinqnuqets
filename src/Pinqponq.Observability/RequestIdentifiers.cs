using Microsoft.AspNetCore.Http;
using Pinqloq;

namespace Pinqponq.Observability;

internal static class RequestIdentifiers
{
    public static string ReadDeviceIdentifier(HttpContext httpContext, string? deviceIdentifierHeader) =>
        string.IsNullOrWhiteSpace(deviceIdentifierHeader)
            ? string.Empty
            : httpContext.Request.Headers[deviceIdentifierHeader].ToString();

    // Mirrors the SDK's own resolution order (see PinqloqRequestLoggingMiddleware.ResolveCorrelationId), so a
    // caller-propagated correlation id survives instead of only the trace id.
    public static string ResolveCorrelationId(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers[PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName].ToString();
        return string.IsNullOrWhiteSpace(header) ? httpContext.TraceIdentifier : header;
    }
}
