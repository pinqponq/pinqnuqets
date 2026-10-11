namespace Pinqponq.Observability;

/// <summary>
/// Configuration bound from the <c>Observability</c> section.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Observability";

    /// <summary>Request and response bodies longer than this are trimmed in the log. Defaults to 5 MiB.</summary>
    public int MaxLoggedBodyBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Whether a request without a device uid (<c>DeviceUid</c> header or query value) is
    /// rejected with 400 <c>DEVICE_UID_REQUIRED</c>. Defaults to false; turn it on only for
    /// services that serve the mobile app alone.
    /// </summary>
    public bool RequireDeviceUid { get; set; }

    /// <summary>Required. Pinqloq collection of the per-request records.</summary>
    public string HttpCollectionName { get; set; } = string.Empty;

    /// <summary>Required. Pinqloq collection of <see cref="ILogService"/> records that name no collection (jobs, background work).</summary>
    public string JobsCollectionName { get; set; } = string.Empty;

    /// <summary>Required. Pinqloq collection of the records relayed by the client-log endpoint.</summary>
    public string ClientCollectionName { get; set; } = string.Empty;

    /// <summary>Request header carrying the caller's device identifier. Defaults to <c>Identifier</c>.</summary>
    public string DeviceIdentifierHeader { get; set; } = "Identifier";

    /// <summary>Requests whose path contains one of these segments are not logged.</summary>
    public IList<string> ExcludedPathSegments { get; set; } = ["/health", "/openapi", "/scalar", "/swagger", ClientLogEndpoint.PathSegment];

    /// <summary>
    /// Requests whose path contains one of these segments skip the device uid check, e.g.
    /// third-party webhooks that cannot send the header.
    /// </summary>
    public IList<string> DeviceUidExemptPathSegments { get; set; } = [];
}
