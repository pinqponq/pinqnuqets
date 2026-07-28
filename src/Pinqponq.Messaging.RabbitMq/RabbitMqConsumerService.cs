using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Pinqponq.Messaging.RabbitMq;

/// <summary>
/// Background consumer that declares its topology (queue + optional dead-letter exchange),
/// dispatches each message to <typeparamref name="THandler"/> in a DI scope, and
/// acks/nacks accordingly. Reconnects with backoff when the channel/connection drops.
/// </summary>
internal sealed class RabbitMqConsumerService<THandler> : BackgroundService
    where THandler : class, IMessageHandler
{
    internal const string RedeliveryHeader = "x-pinqponq-redelivery";

    private static readonly CreateChannelOptions ConfirmChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqConsumerOptions _consumer;
    private readonly ILogger<RabbitMqConsumerService<THandler>> _logger;
    private readonly ConcurrentDictionary<ulong, Task> _inFlight = new();
    private IChannel? _channel;
    private string? _consumerTag;

    public RabbitMqConsumerService(
        IRabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        RabbitMqConsumerOptions consumer,
        ILogger<RabbitMqConsumerService<THandler>> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _consumer = consumer;
        _logger = logger;

        if (_consumer.MaxRedeliveryCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consumer),
                $"{nameof(RabbitMqConsumerOptions.MaxRedeliveryCount)} must be at least 1.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerSessionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RabbitMQ consumer session failed on queue {Queue}; reconnecting.",
                    _consumer.Queue);
            }

            await TeardownChannelAsync().ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunConsumerSessionAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection
            .CreateChannelAsync(ConfirmChannelOptions, stoppingToken)
            .ConfigureAwait(false);
        var channelClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _channel.ChannelShutdownAsync += (_, _) =>
        {
            channelClosed.TrySetResult();
            return Task.CompletedTask;
        };

        var routingKey = string.IsNullOrEmpty(_consumer.RoutingKey) ? _consumer.Queue : _consumer.RoutingKey;
        var arguments = new Dictionary<string, object?>();

        if (_consumer.EnableDeadLetter)
        {
            var deadLetterExchange = _consumer.DeadLetterExchange ?? _consumer.Queue + ".dlx";
            var deadLetterQueue = _consumer.DeadLetterQueue ?? _consumer.Queue + ".dead";

            await _channel.ExchangeDeclareAsync(
                deadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: stoppingToken).ConfigureAwait(false);
            await _channel.QueueDeclareAsync(
                deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
                cancellationToken: stoppingToken).ConfigureAwait(false);
            await _channel.QueueBindAsync(
                deadLetterQueue, deadLetterExchange, routingKey: string.Empty,
                cancellationToken: stoppingToken).ConfigureAwait(false);

            arguments["x-dead-letter-exchange"] = deadLetterExchange;
        }

        if (!string.IsNullOrEmpty(_consumer.Exchange))
        {
            await _channel.ExchangeDeclareAsync(
                _consumer.Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
                cancellationToken: stoppingToken).ConfigureAwait(false);
        }

        await _channel.QueueDeclareAsync(
            _consumer.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments.Count > 0 ? arguments : null,
            cancellationToken: stoppingToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_consumer.Exchange))
        {
            await _channel.QueueBindAsync(
                _consumer.Queue, _consumer.Exchange, routingKey,
                cancellationToken: stoppingToken).ConfigureAwait(false);
        }

        await _channel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var task = HandleAsync(eventArgs, stoppingToken);
            _inFlight[eventArgs.DeliveryTag] = task;
            try
            {
                await task.ConfigureAwait(false);
            }
            finally
            {
                _inFlight.TryRemove(eventArgs.DeliveryTag, out Task? _);
            }
        };

        _consumerTag = await _channel.BasicConsumeAsync(
            _consumer.Queue, autoAck: false, consumer, stoppingToken).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var reg = stoppingToken.Register(() => channelClosed.TrySetResult());
        await channelClosed.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    private async Task HandleAsync(BasicDeliverEventArgs eventArgs, CancellationToken stoppingToken)
    {
        var channel = _channel;
        if (channel is null)
        {
            return;
        }

        try
        {
            var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            await handler.HandleAsync(message, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            try
            {
                await channel.BasicNackAsync(
                        eventArgs.DeliveryTag, multiple: false, requeue: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "Failed to requeue message on queue {Queue} during shutdown.", _consumer.Queue);
            }

            return;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(channel, eventArgs, ex).ConfigureAwait(false);
            return;
        }

        try
        {
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ackEx)
        {
            _logger.LogError(ackEx, "Failed to ack message on queue {Queue} after successful handling.", _consumer.Queue);
        }
    }

    private async Task HandleFailureAsync(IChannel channel, BasicDeliverEventArgs eventArgs, Exception ex)
    {
        if (_consumer.EnableDeadLetter)
        {
            _logger.LogError(ex, "Message handling failed on queue {Queue}; routing to dead-letter.", _consumer.Queue);
            try
            {
                await channel.BasicNackAsync(
                        eventArgs.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "Failed to nack message on queue {Queue}.", _consumer.Queue);
            }

            return;
        }

        var count = ReadRedeliveryCount(eventArgs.BasicProperties);
        if (count >= _consumer.MaxRedeliveryCount)
        {
            _logger.LogError(
                ex,
                "Message handling failed on queue {Queue}; dropping after {Count} redeliveries.",
                _consumer.Queue,
                count);
            try
            {
                await channel.BasicNackAsync(
                        eventArgs.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "Failed to drop poison message on queue {Queue}.", _consumer.Queue);
            }

            return;
        }

        _logger.LogError(
            ex,
            "Message handling failed on queue {Queue}; republishing (redelivery {Next}/{Max}).",
            _consumer.Queue,
            count + 1,
            _consumer.MaxRedeliveryCount);

        try
        {
            var headers = CopyHeaders(eventArgs.BasicProperties.Headers);
            headers[RedeliveryHeader] = count + 1;
            var properties = new BasicProperties
            {
                Persistent = true,
                Headers = headers,
            };

            await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: _consumer.Queue,
                    mandatory: true,
                    basicProperties: properties,
                    body: eventArgs.Body,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception republishEx)
        {
            _logger.LogError(
                republishEx,
                "Failed to republish message on queue {Queue}; dropping to avoid an infinite requeue loop.",
                _consumer.Queue);
            try
            {
                await channel.BasicNackAsync(
                        eventArgs.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "Failed to drop message on queue {Queue} after republish error.", _consumer.Queue);
            }
        }
    }

    internal static int ReadRedeliveryCount(IReadOnlyBasicProperties? properties)
    {
        if (properties?.Headers is null
            || !properties.Headers.TryGetValue(RedeliveryHeader, out var raw)
            || raw is null)
        {
            return 0;
        }

        return raw switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            byte[] bytes when bytes.Length > 0 => bytes[0],
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }

    private static Dictionary<string, object?> CopyHeaders(IDictionary<string, object?>? source)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (source is null)
        {
            return headers;
        }

        foreach (var pair in source)
        {
            headers[pair.Key] = pair.Value;
        }

        return headers;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await TeardownChannelAsync().ConfigureAwait(false);
    }

    private async Task TeardownChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        var channel = _channel;
        var tag = _consumerTag;
        _consumerTag = null;

        if (!string.IsNullOrEmpty(tag))
        {
            try
            {
                await channel.BasicCancelAsync(tag).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error cancelling RabbitMQ consumer on {Queue}.", _consumer.Queue);
            }
        }

        if (!_inFlight.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_inFlight.Values).WaitAsync(DrainTimeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Timed out draining in-flight handlers for {Queue}; nacking remaining deliveries.",
                    _consumer.Queue);

                foreach (var deliveryTag in _inFlight.Keys.ToArray())
                {
                    try
                    {
                        await channel.BasicNackAsync(
                                deliveryTag, multiple: false, requeue: true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception nackEx)
                    {
                        _logger.LogDebug(
                            nackEx,
                            "Failed to nack in-flight delivery {DeliveryTag} on {Queue}.",
                            deliveryTag,
                            _consumer.Queue);
                    }
                }
            }
        }

        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing RabbitMQ consumer channel for {Queue}.", _consumer.Queue);
        }

        _channel = null;
    }
}
