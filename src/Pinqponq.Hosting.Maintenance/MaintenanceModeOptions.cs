using Microsoft.AspNetCore.Http;

namespace Pinqponq.Hosting.Maintenance;

/// <summary>Maintenance mode of an application. Validated at startup and on every configuration reload.</summary>
public sealed class MaintenanceModeOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "MaintenanceMode";

    /// <summary>Default of <see cref="Message"/>.</summary>
    public const string DefaultMessage = "The service is temporarily unavailable because of maintenance.";

    /// <summary>Default entry of <see cref="ExemptPathPrefixes"/>.</summary>
    public const string DefaultExemptPathPrefix = "/health";

    /// <summary>
    /// While <see langword="true"/>, every request outside <see cref="ExemptPathPrefixes"/> is answered with the
    /// error body and does not reach the rest of the pipeline. Read on every request, so a configuration reload
    /// turns maintenance mode on or off without a restart. Defaults to <see langword="false"/>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Message of the error body. With error localization on and <see cref="LocalizeMessage"/> on, the catalog
    /// message for <see cref="ResponseCode"/> is written instead. Defaults to <see cref="DefaultMessage"/>, also
    /// when configuration sets it to <see langword="null"/>.
    /// </summary>
    public string Message { get; set; } = DefaultMessage;

    /// <summary>
    /// While <see langword="true"/> and error localization is on, the catalog message for
    /// <see cref="ResponseCode"/> replaces <see cref="Message"/>. Set it to <see langword="false"/> to always
    /// write <see cref="Message"/>. Defaults to <see langword="true"/>. While it is <see langword="false"/>, a custom
    /// <c>IErrorResponseWriter</c> must implement <c>WriteMessage</c>; otherwise every blocked request fails.
    /// </summary>
    public bool LocalizeMessage { get; set; } = true;

    /// <summary>
    /// Response code of the error body, such as <c>planned_maintenance</c>. Required: it has no default,
    /// because each application has its own code.
    /// </summary>
    public string? ResponseCode { get; set; }

    /// <summary>
    /// Log event name of a blocked request on <c>Pinqponq.Contracts.IRequestErrorFeature</c>. Defaults to
    /// <see cref="MaintenanceModeEvents.RequestBlockedByMaintenance"/>.
    /// </summary>
    public string EventName { get; set; } = MaintenanceModeEvents.RequestBlockedByMaintenance;

    /// <summary>HTTP status code of a blocked request, from 400 to 599. Defaults to 503.</summary>
    public int StatusCode { get; set; } = StatusCodes.Status503ServiceUnavailable;

    /// <summary>
    /// When set, a blocked request gets a <c>Retry-After</c> header with this number of seconds. Must be greater
    /// than zero. Not set by default, so no header is sent.
    /// </summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Path prefixes that keep working during maintenance, such as health checks and webhooks. A prefix matches
    /// whole path segments without regard to case, so <c>/health</c> matches <c>/health</c> and
    /// <c>/health/ready</c> but not <c>/healthz</c>. Each entry starts with <c>/</c>. Contains
    /// <see cref="DefaultExemptPathPrefix"/> by default. Entries bound from configuration are added to the
    /// default entry; clear the list in code to remove it.
    /// </summary>
    public IList<string> ExemptPathPrefixes { get; } = [DefaultExemptPathPrefix];
}
