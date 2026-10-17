using Microsoft.AspNetCore.Http;

namespace Pinqponq.Observability;

internal static class DeviceIdentifier
{
    public static string Read(HttpContext httpContext, string? deviceIdentifierHeader) =>
        string.IsNullOrWhiteSpace(deviceIdentifierHeader)
            ? string.Empty
            : httpContext.Request.Headers[deviceIdentifierHeader].ToString();
}
