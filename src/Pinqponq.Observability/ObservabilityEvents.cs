namespace Pinqponq.Observability;

/// <summary>
/// Log event names written by this package.
/// </summary>
public static class ObservabilityEvents
{
    /// <summary>A request was rejected because it had no device uid.</summary>
    public const string RequestRejectedDeviceUidMissing = "request_rejected_device_uid_missing";
}
