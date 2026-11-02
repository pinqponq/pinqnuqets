namespace Pinqponq.Hosting.Maintenance;

/// <summary>Log event names this package puts on <c>Pinqponq.Contracts.IRequestErrorFeature</c>.</summary>
public static class MaintenanceModeEvents
{
    /// <summary>Maintenance mode blocked the request. Default of <see cref="MaintenanceModeOptions.EventName"/>.</summary>
    public const string RequestBlockedByMaintenance = "request_blocked_by_maintenance";
}
