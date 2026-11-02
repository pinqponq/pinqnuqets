using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.ErrorHandling;

namespace Pinqponq.Hosting.Maintenance;

internal sealed class MaintenanceModeMiddleware(
    RequestDelegate next,
    IOptionsMonitor<MaintenanceModeOptions> optionsMonitor,
    IErrorResponseWriter errorResponseWriter,
    ILogger<MaintenanceModeMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled || IsExemptPath(context.Request.Path, options))
        {
            return next(context);
        }

        return WriteBlockedResponse(context, options);
    }

    private static bool IsExemptPath(PathString path, MaintenanceModeOptions options) =>
        options.ExemptPathPrefixes.Any(pathPrefix => path.StartsWithSegments(pathPrefix, StringComparison.OrdinalIgnoreCase));

    private Task WriteBlockedResponse(HttpContext context, MaintenanceModeOptions options)
    {
        logger.LogWarning(
            "Maintenance mode blocked {RequestMethod} {RequestPath}.",
            context.Request.Method,
            context.Request.Path.Value);

        if (options.RetryAfterSeconds is int retryAfterSeconds)
        {
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(NumberFormatInfo.InvariantInfo);
        }

        return options.LocalizeMessage
            ? errorResponseWriter.Write(context, options.StatusCode, options.ResponseCode, options.Message, options.EventName)
            : errorResponseWriter.WriteMessage(context, options.StatusCode, options.ResponseCode, options.Message, options.EventName);
    }
}
