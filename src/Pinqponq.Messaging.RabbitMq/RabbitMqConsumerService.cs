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
/// acks/nacks accordingly.
/// </summary>
internal sealed class RabbitMqConsumerService<THandler> : BackgroundService
    where THandler : class, IMessageHandler
{
    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqConsumerOptions _consumer;
    private readonly ILogger<RabbitMqConsumerService<THandler>> _logger;
    private IChannel? _channel;

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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(stoppingToken).ConfigureAwait(false);

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
        consumer.ReceivedAsync += (sender, eventArgs) => HandleAsync(eventArgs, stoppingToken);

        await _channel.BasicConsumeAsync(
            _consumer.Queue, autoAck: false, consumer, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleAsync(BasicDeliverEventArgs eventArgs, CancellationToken stoppingToken)
    {
        var channel = _channel!;
        try
        {
            var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            await handler.HandleAsync(message, stoppingToken).ConfigureAwait(false);

            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Message handling failed on queue {Queue}; routing to dead-letter.",
                _consumer.Queue);

            // requeue:false → routed to the dead-letter exchange when configured.
            await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
