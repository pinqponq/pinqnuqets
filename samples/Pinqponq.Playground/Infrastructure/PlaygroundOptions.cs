namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Console-level settings, bound from the <c>Playground</c> configuration section.
/// </summary>
public sealed class PlaygroundOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Playground";

    /// <summary>How many log entries the console retains for the history view.</summary>
    public int LogBufferCapacity { get; set; } = 5000;

    /// <summary>Default per-scenario timeout in seconds.</summary>
    public int ScenarioTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Endpoints supplied from outside (docker-compose, a shared dev server). A service
    /// listed here is reported as <see cref="DevServiceState.External"/> and never
    /// provisioned as a container.
    /// </summary>
    /// <remarks>
    /// Keys are the <see cref="DevServiceIds"/> values. Postgres/Redis/Mongo/MSSQL take a
    /// connection string, RabbitMQ an <c>amqp://user:pass@host:port</c> URI and MailHog a
    /// <c>host:smtpPort:apiPort</c> triple.
    /// </remarks>
    public Dictionary<string, string> ExternalServices { get; } = new(StringComparer.Ordinal);
}
