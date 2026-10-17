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

    /// <summary>Required. Pinqloq collection of the per-request records.</summary>
    public string HttpCollectionName { get; set; } = string.Empty;

    /// <summary>Required. Pinqloq collection of <see cref="ILogService"/> records that name no collection (jobs, background work).</summary>
    public string JobsCollectionName { get; set; } = string.Empty;

    /// <summary>
    /// Request header whose value becomes the record's device identifier. Defaults to
    /// <see langword="null"/>: no header is read and records carry no device identifier.
    /// </summary>
    public string? DeviceIdentifierHeader { get; set; }

    /// <summary>
    /// Body, form and query keys redacted on top of the built-in ones (<c>Authorization</c>,
    /// <c>Cookie</c>, <c>Set-Cookie</c>, <c>ApiKey</c>, <c>X-Api-Key</c> and keys containing
    /// <c>secret</c>, <c>token</c> or <c>password</c>). Compared case-insensitively.
    /// </summary>
    public IList<string> RedactedKeys { get; set; } = [];

    /// <summary>
    /// <c>HttpContext.Items</c> entries added to the request record: item key → record field.
    /// An item that is missing or empty adds nothing.
    /// </summary>
    public IDictionary<string, string> LoggedContextItems { get; set; } = new Dictionary<string, string>();

    /// <summary>Requests whose path contains one of these segments are not logged.</summary>
    public IList<string> ExcludedPathSegments { get; set; } = ["/health", "/openapi", "/scalar", "/swagger"];
}
