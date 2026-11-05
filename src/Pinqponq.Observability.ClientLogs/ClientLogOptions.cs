namespace Pinqponq.Observability.ClientLogs;

/// <summary>
/// Settings of the client log endpoints. A limit that is not set is not checked.
/// </summary>
public sealed class ClientLogOptions
{
    /// <summary>Required. Pinqloq collection the client logs are written to. The client cannot choose it.</summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>Most entries one batch may hold. A larger batch is refused with 400.</summary>
    public int? MaxEntriesPerBatch { get; set; }

    /// <summary>Most bytes one request body may hold. A larger body is refused with 413 and is not read further.</summary>
    public int? MaxBodyBytes { get; set; }

    /// <summary>Most metadata and detail fields one entry may hold, counted together. A larger entry is refused with 400.</summary>
    public int? MaxFieldsPerEntry { get; set; }

    /// <summary>
    /// Longest event name, field value and device identifier. A longer event name is refused with 400;
    /// a longer field value or device identifier is cut to this length.
    /// </summary>
    public int? MaxValueLength { get; set; }

    /// <summary>
    /// Metadata and detail keys that contain one of these parts (compared case-insensitively) are redacted.
    /// Keys listed in <see cref="ObservabilityOptions.RedactedKeys"/> are always redacted.
    /// </summary>
    public IList<string> RedactedKeyParts { get; set; } = [];
}
