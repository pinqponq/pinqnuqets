namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Handles a single consumed message. Throwing rejects the message; when dead-lettering
/// is enabled the message is routed to the dead-letter exchange instead of being requeued.
/// </summary>
public interface IMessageHandler
{
    /// <summary>Handles the message body decoded as a UTF-8 string.</summary>
    Task HandleAsync(string message, CancellationToken cancellationToken);
}
