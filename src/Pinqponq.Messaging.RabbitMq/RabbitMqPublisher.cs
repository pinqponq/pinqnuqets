using System.Text;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Default <see cref="IMessagePublisher"/> over <see cref="IRabbitMqConnection"/> with a
/// Polly retry pipeline.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the publisher from the connection and configured options.</summary>
    public RabbitMqPublisher(IRabbitMqConnection connection, IOptions<RabbitMqOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = value.PublishRetryCount,
                Delay = value.PublishRetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    /// <inheritdoc />
    public Task PublishAsync(
        string exchange,
        string routingKey,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PublishAsync(exchange, routingKey, Encoding.UTF8.GetBytes(message), cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(routingKey);

        await _pipeline.ExecuteAsync(
            async token =>
            {
                await using var channel = await _connection.CreateChannelAsync(token).ConfigureAwait(false);
                var properties = new BasicProperties { Persistent = true };
                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
