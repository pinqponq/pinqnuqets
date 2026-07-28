using RabbitMQ.Client;

namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Owns a single recovering RabbitMQ connection and hands out channels.
/// </summary>
public interface IRabbitMqConnection : IAsyncDisposable
{
    /// <summary>Opens a new channel on the shared connection.</summary>
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a new channel with the given creation options (e.g. publisher confirms).</summary>
    Task<IChannel> CreateChannelAsync(
        CreateChannelOptions options,
        CancellationToken cancellationToken = default);
}
