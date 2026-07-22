namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Per-consumer topology. Dead-lettering is enabled by default; failed messages are
/// routed to a dead-letter exchange/queue rather than requeued.
/// </summary>
public sealed class RabbitMqConsumerOptions
{
    /// <summary>The queue to consume from (and declare).</summary>
    public required string Queue { get; set; }

    /// <summary>Exchange to bind the queue to. Empty uses the default exchange.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Routing key for the binding. Defaults to <see cref="Queue"/>.</summary>
    public string? RoutingKey { get; set; }

    /// <summary>Whether to declare and route failures to a dead-letter exchange. Defaults to true.</summary>
    public bool EnableDeadLetter { get; set; } = true;

    /// <summary>Dead-letter exchange name. Defaults to <c>{Queue}.dlx</c>.</summary>
    public string? DeadLetterExchange { get; set; }

    /// <summary>Dead-letter queue name. Defaults to <c>{Queue}.dead</c>.</summary>
    public string? DeadLetterQueue { get; set; }
}
