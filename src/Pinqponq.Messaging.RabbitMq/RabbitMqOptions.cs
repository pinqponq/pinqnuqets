namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Connection and resiliency configuration for RabbitMQ.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>Broker host name.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Broker port.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>User name.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>Password.</summary>
    public string Password { get; set; } = "guest";

    /// <summary>Virtual host.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Consumer prefetch count (QoS). Defaults to 10.</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>Maximum retry attempts when publishing. Defaults to 3.</summary>
    public int PublishRetryCount { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between publish retries. Defaults to 200ms.</summary>
    public TimeSpan PublishRetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
