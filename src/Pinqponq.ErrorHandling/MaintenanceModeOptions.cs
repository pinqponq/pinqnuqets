namespace Pinqponq.ErrorHandling;

/// <summary>
/// Maintenance mode: while enabled every request is answered 503 <c>system_maintenance</c>.
/// Bound by <c>AddPinqponqMaintenanceMode</c> and read per request by <see cref="MaintenanceModeMiddleware"/>, so a configuration reload
/// switches it without a restart.
/// </summary>
public sealed class MaintenanceModeOptions
{
    /// <summary>Whether maintenance mode is on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Response message. Defaults to <c>System is under maintenance</c>.</summary>
    public string? Message { get; set; }
}
