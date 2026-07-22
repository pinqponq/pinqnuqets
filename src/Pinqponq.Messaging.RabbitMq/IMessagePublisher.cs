namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Publishes messages to RabbitMQ. Messages are persistent and publishing is retried
/// on transient failures.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publishes a raw payload.</summary>
    Task PublishAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes a UTF-8 string payload.</summary>
    Task PublishAsync(
        string exchange,
        string routingKey,
        string message,
        CancellationToken cancellationToken = default);
}
