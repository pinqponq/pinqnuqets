using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// While <see cref="MaintenanceModeOptions.Enabled"/> is on, answers every request 503
/// <c>system_maintenance</c> without running the rest of the pipeline.
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    private const string DefaultMaintenanceMessage = "System is under maintenance";

    private readonly RequestDelegate _next;
    private readonly ErrorHandlingOptions _options;
    private readonly IOptionsMonitor<MaintenanceModeOptions> _maintenanceModeOptions;
    private readonly ILogger<MaintenanceModeMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public MaintenanceModeMiddleware(
        RequestDelegate next,
        IOptions<ErrorHandlingOptions> options,
        IOptionsMonitor<MaintenanceModeOptions> maintenanceModeOptions,
        ILogger<MaintenanceModeMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _maintenanceModeOptions = maintenanceModeOptions;
        _logger = logger;
    }

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var maintenanceMode = _maintenanceModeOptions.CurrentValue;
        if (!maintenanceMode.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var message = maintenanceMode.Message ?? DefaultMaintenanceMessage;
        _logger.LogWarning("Request blocked by maintenance mode. {Method} {Path}", context.Request.Method, context.Request.Path.Value);

        RequestErrorDetail.Describe(
            context,
            ErrorHandlingEvents.RequestBlockedMaintenanceMode,
            StatusCodes.Status503ServiceUnavailable,
            CommonErrorCodes.SystemMaintenance,
            message);
        await ErrorResponseWriter.Write(
            context,
            _options,
            StatusCodes.Status503ServiceUnavailable,
            CommonErrorCodes.SystemMaintenance,
            message).ConfigureAwait(false);
    }
}
