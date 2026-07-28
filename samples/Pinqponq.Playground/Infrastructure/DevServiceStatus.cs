namespace Pinqponq.Playground.Infrastructure;

/// <summary>Lifecycle state of a dev-stack service.</summary>
public enum DevServiceState
{
    /// <summary>No container running; the console can start one.</summary>
    Stopped,

    /// <summary>A container is being pulled and started.</summary>
    Starting,

    /// <summary>Reachable; scenarios that need it can run.</summary>
    Ready,

    /// <summary>The last start attempt failed; see <see cref="DevServiceStatus.LastError"/>.</summary>
    Failed,

    /// <summary>No Docker daemon is reachable, so no container can be started.</summary>
    DockerUnavailable,

    /// <summary>An externally supplied endpoint is configured; nothing is provisioned.</summary>
    External,
}

/// <summary>Snapshot of one dev-stack service, as rendered by the console.</summary>
public sealed record DevServiceStatus
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Turkish one-liner explaining which packages depend on this service.</summary>
    public required string Description { get; init; }

    public required string Image { get; init; }

    public required DevServiceState State { get; init; }

    /// <summary>True for images that are large or platform-restricted (SQL Server).</summary>
    public bool Heavy { get; init; }

    public string? ConnectionString { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>How long the last successful start took.</summary>
    public long? StartupMs { get; init; }

    public string? LastError { get; init; }

    public string? ContainerId { get; init; }
}

/// <summary>Endpoint details resolved once a service is running.</summary>
public sealed record DevEndpoint(
    string? ConnectionString,
    string? Host,
    int? Port,
    IReadOnlyDictionary<string, string> Extra)
{
    /// <summary>An endpoint carrying only a connection string.</summary>
    public static DevEndpoint FromConnectionString(string connectionString) =>
        new(connectionString, null, null, new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Thrown when a scenario asks for a service that is not ready. The message is shown to
/// the user verbatim, so it is written in Turkish like the rest of the console.
/// </summary>
public sealed class DevStackNotReadyException(string message) : InvalidOperationException(message);
