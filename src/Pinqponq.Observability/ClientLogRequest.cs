using Pinqloq;

namespace Pinqponq.Observability;

/// <summary>
/// Body of the client-log relay endpoint: one log record sent by a mobile or web client.
/// </summary>
public sealed class ClientLogRequest
{
    /// <summary>Event name.</summary>
    public required string Event { get; set; }

    /// <summary><c>Debug</c>, <c>Information</c> (default), <c>Warning</c>/<c>Warn</c>, <c>Error</c> or <c>Fatal</c>.</summary>
    public string? LogLevel { get; set; } = nameof(PinqloqLogLevel.Information);

    /// <summary>Record metadata.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Record detail.</summary>
    public Dictionary<string, string>? Detail { get; set; }

    /// <summary>When the client produced the record; <see langword="null"/> uses the receive time.</summary>
    public DateTime? Date { get; set; }
}
