using System.Text;
using System.Threading.Channels;
using Pinqponq.Messaging.RabbitMq;
using Pinqponq.Messaging.RabbitMq.DependencyInjection;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Messaging.RabbitMq</c>.</summary>
public static class RabbitMqScenarios
{
    private const string Package = "Pinqponq.Messaging.RabbitMq";

    public static IEnumerable<Scenario> Create()
    {
        yield return RoundTrip();
        yield return DeadLetter();
        yield return DropWithoutDeadLetter();
        yield return QueueRequired();
        yield return PublishRetry();
    }

    private static Scenario RoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.roundtrip",
            PackageId = Package,
            Title = "Publish → consume round trip",
            Summary = "Starts a real hosted consumer, publishes a message, and waits for it to "
                      + "reach the handler. The consumer's readiness is determined by waiting for "
                      + "BasicConsume to complete — not by a fixed delay.",
            RequiredServices = [DevServiceIds.RabbitMq],
            Fields =
            [
                new ScenarioField("message", "Message", ScenarioFieldKind.Text, "hello rabbit"),
                new ScenarioField("prefetchCount", "PrefetchCount", ScenarioFieldKind.Number, "10"),
            ],
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var queue = $"pg-{Guid.NewGuid():N}"[..16];
            var rendezvous = new MessageRendezvous();

            await using var host = context.Host(services =>
            {
                services.AddSingleton(rendezvous);
                ConfigureBroker(services, context);
                services.AddRabbitMqConsumer<RendezvousHandler>(consumer =>
                {
                    consumer.Queue = queue;
                    consumer.EnableDeadLetter = true;
                });
            });

            await host.StartHostedServicesAsync(context.CancellationToken);
            context.Step("Hosted consumer started", $"queue {queue}");

            var ready = await WaitForConsumerAsync(host, queue, context);
            context.Require("Consumer connected to the queue", ready);

            var message = context.Input.Text("message");
            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, queue, message, context.CancellationToken);
            context.Step("Message published");

            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var received = await rendezvous.Reader.ReadAsync(receiveTimeout.Token);

            context.Require("Handler received the message", received == message, received);
            context.Artifact("round trip", new { queue, sent = message, received });
        });

    private static Scenario DeadLetter() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.dead-letter",
            PackageId = Package,
            Title = "A message lands in the dead-letter queue when the handler fails",
            Summary = "The handler throws; the package nacks the message without requeuing and the "
                      + "DLX moves it to the {queue}.dead queue. The structured error log the package "
                      + "produces is also verified.",
            RequiredServices = [DevServiceIds.RabbitMq],
            NegativePath = true,
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var queue = $"pg-dl-{Guid.NewGuid():N}"[..18];
            var rendezvous = new MessageRendezvous { ThrowOnHandle = true };

            await using var host = context.Host(services =>
            {
                services.AddSingleton(rendezvous);
                ConfigureBroker(services, context);
                services.AddRabbitMqConsumer<RendezvousHandler>(consumer =>
                {
                    consumer.Queue = queue;
                    consumer.EnableDeadLetter = true;
                });
            });

            await host.StartHostedServicesAsync(context.CancellationToken);
            context.Require("Consumer connected to the queue", await WaitForConsumerAsync(host, queue, context));

            const string Payload = "this message will not be processed";
            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, queue, Payload, context.CancellationToken);
            context.Step("Message published", $"queue {queue}");

            using (var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                receiveTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                var seen = await rendezvous.Reader.ReadAsync(receiveTimeout.Token);
                context.Step("Handler saw the message and threw", seen);
            }

            var deadLetterQueue = $"{queue}.dead";
            var deadLettered = await WaitForDeadLetterAsync(host, deadLetterQueue, context);

            context.Require("Message landed in the dead-letter queue", deadLettered is not null, deadLetterQueue);
            context.Require("Body preserved", deadLettered == Payload, deadLettered);

            var logEntry = await context.WaitForLogAsync(
                record => record.MessageTemplate?.Contains("dead-letter", StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(10));

            context.Require("The package produced the error log", logEntry is not null);
            context.Check(
                "The log record has a structured Queue field",
                logEntry!.State.TryGetValue("Queue", out var loggedQueue)
                && string.Equals(loggedQueue?.ToString(), queue, StringComparison.Ordinal),
                logEntry.State.TryGetValue("Queue", out var value) ? value?.ToString() : "(none)");

            context.Artifact("log record", new
            {
                level = logEntry.Level,
                category = logEntry.Category,
                messageTemplate = logEntry.MessageTemplate,
                state = logEntry.State,
                exception = logEntry.Exception?.Type,
            });
        });

    private static Scenario DropWithoutDeadLetter() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.drop-no-dlx",
            PackageId = Package,
            Title = "A poison message is dropped when the DLX is disabled",
            Summary = "When EnableDeadLetter=false and the handler keeps failing, the package "
                      + "redelivers the message up to MaxRedeliveryCount times, then drops it via "
                      + "nack(requeue:false) and produces a 'dropping after' log.",
            RequiredServices = [DevServiceIds.RabbitMq],
            NegativePath = true,
            Fields =
            [
                new ScenarioField("maxRedelivery", "MaxRedeliveryCount", ScenarioFieldKind.Number, "2"),
            ],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            var queue = $"pg-drop-{Guid.NewGuid():N}"[..18];
            var rendezvous = new MessageRendezvous { ThrowOnHandle = true };
            var maxRedelivery = context.Input.Int("maxRedelivery");

            await using var host = context.Host(services =>
            {
                services.AddSingleton(rendezvous);
                ConfigureBroker(services, context);
                services.AddRabbitMqConsumer<RendezvousHandler>(consumer =>
                {
                    consumer.Queue = queue;
                    consumer.EnableDeadLetter = false;
                    consumer.MaxRedeliveryCount = maxRedelivery;
                });
            });

            await host.StartHostedServicesAsync(context.CancellationToken);
            context.Require("Consumer connected", await WaitForConsumerAsync(host, queue, context));

            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, queue, "poison", context.CancellationToken);
            context.Step("Poison message published");

            var dropLog = await context.WaitForLogAsync(
                record => record.MessageTemplate?.Contains("dropping after", StringComparison.OrdinalIgnoreCase) == true
                          || record.Message?.Contains("dropping after", StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(45));

            context.Require("Drop log produced", dropLog is not null);
            context.Artifact("log", new
            {
                dropLog!.Level,
                dropLog.MessageTemplate,
                dropLog.Message,
                dropLog.State,
            });
        });

    private static Scenario QueueRequired() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.queue-required",
            PackageId = Package,
            Title = "An empty queue name fails at registration time",
            Summary = "AddRabbitMqConsumer validates the queue name at registration time, not at "
                      + "runtime. No broker is needed — the error occurs during DI setup.",
            NegativePath = true,
        },
        context =>
        {
            Exception? thrown = null;
            try
            {
                var services = new ServiceCollection();
                services.AddPinqponqRabbitMq(broker => broker.HostName = "localhost");
                services.AddRabbitMqConsumer<RendezvousHandler>(consumer => consumer.Queue = string.Empty);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("InvalidOperationException at registration time", thrown is not null);
            context.Check(
                "The error names the Queue field",
                thrown!.Message.Contains("Queue", StringComparison.Ordinal),
                thrown.Message);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });

            return Task.CompletedTask;
        });

    private static Scenario PublishRetry() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.publish-retry",
            PackageId = Package,
            Title = "Publish retries when the broker is unavailable",
            Summary = "Publishes to an unreachable broker. As PublishRetryCount increases, the "
                      + "elapsed time grows noticeably; the error eventually propagates to the caller.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("publishRetryCount", "PublishRetryCount", ScenarioFieldKind.Number, "2"),
                new ScenarioField("retryBaseDelayMs", "PublishRetryBaseDelay (ms)", ScenarioFieldKind.Duration, "200"),
            ],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            var withoutRetry = await MeasurePublishFailureAsync(context, 0);
            context.Step($"PublishRetryCount=0 → {withoutRetry.ElapsedMs} ms", withoutRetry.ExceptionType);
            context.Require("Publish without retries failed", withoutRetry.ExceptionType is not null);

            var retryCount = context.Input.Int("publishRetryCount");
            var withRetry = await MeasurePublishFailureAsync(context, retryCount);
            context.Step($"PublishRetryCount={retryCount} → {withRetry.ElapsedMs} ms", withRetry.ExceptionType);
            context.Require("Publish with retries also failed", withRetry.ExceptionType is not null);

            context.Require(
                "The retries added a measurable amount of extra time",
                withRetry.ElapsedMs > withoutRetry.ElapsedMs,
                $"{withoutRetry.ElapsedMs} ms → {withRetry.ElapsedMs} ms");

            context.Artifact("measurement", new
            {
                retry0Ms = withoutRetry.ElapsedMs,
                retryNMs = withRetry.ElapsedMs,
                retryCount,
                exceptionType = withRetry.ExceptionType,
            });
        });

    private static void ConfigureBroker(IServiceCollection services, ScenarioContext context)
    {
        var endpoint = context.Stack.Require(DevServiceIds.RabbitMq);
        services.AddPinqponqRabbitMq(broker =>
        {
            broker.HostName = endpoint.Host!;
            broker.Port = endpoint.Port!.Value;
            broker.UserName = endpoint.Extra.TryGetValue("userName", out var user) ? user : "guest";
            broker.Password = endpoint.Extra.TryGetValue("password", out var pass) ? pass : "guest";
            broker.PrefetchCount = 10;
        });
    }

    private static async Task<bool> WaitForConsumerAsync(ScenarioHost host, string queue, ScenarioContext context)
    {
        // BackgroundService.StartAsync returns at the first await, well before BasicConsume
        // has actually registered — so poll the queue rather than sleeping a fixed amount.
        var connection = host.GetRequiredService<IRabbitMqConnection>();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(context.CancellationToken);
                var declared = await channel.QueueDeclarePassiveAsync(queue, context.CancellationToken);
                if (declared.ConsumerCount > 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // The queue does not exist yet; the consumer declares it on startup.
            }

            await Task.Delay(250, context.CancellationToken);
        }

        return false;
    }

    private static async Task<string?> WaitForDeadLetterAsync(ScenarioHost host, string queue, ScenarioContext context)
    {
        var connection = host.GetRequiredService<IRabbitMqConnection>();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(context.CancellationToken);
                var result = await channel.BasicGetAsync(queue, autoAck: true, context.CancellationToken);
                if (result is not null)
                {
                    return Encoding.UTF8.GetString(result.Body.ToArray());
                }
            }
            catch (Exception)
            {
                // The dead-letter queue may not be declared yet.
            }

            await Task.Delay(250, context.CancellationToken);
        }

        return null;
    }

    private static async Task<PublishMeasurement> MeasurePublishFailureAsync(ScenarioContext context, int retryCount)
    {
        await using var host = context.Host(services => services.AddPinqponqRabbitMq(broker =>
        {
            broker.HostName = "127.0.0.1";
            broker.Port = 1;
            broker.PublishRetryCount = retryCount;
            broker.PublishRetryBaseDelay = context.Input.Duration("retryBaseDelayMs");
        }));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? exceptionType = null;

        try
        {
            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, "yok", "mesaj", context.CancellationToken);
        }
        catch (Exception exception)
        {
            exceptionType = exception.GetType().Name;
        }

        stopwatch.Stop();
        return new PublishMeasurement(stopwatch.ElapsedMilliseconds, exceptionType);
    }

    private sealed record PublishMeasurement(long ElapsedMs, string? ExceptionType);
}

/// <summary>Lets a scenario observe what the consumer's handler received.</summary>
public sealed class MessageRendezvous
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    /// <summary>Messages the handler has seen.</summary>
    public ChannelReader<string> Reader => _channel.Reader;

    /// <summary>Writer used by the handler.</summary>
    public ChannelWriter<string> Writer => _channel.Writer;

    /// <summary>When set, the handler throws after recording — driving the dead-letter path.</summary>
    public bool ThrowOnHandle { get; init; }
}

/// <summary>Handler the console registers with <c>AddRabbitMqConsumer</c>.</summary>
public sealed class RendezvousHandler(MessageRendezvous rendezvous) : IMessageHandler
{
    /// <inheritdoc />
    public Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        rendezvous.Writer.TryWrite(message);

        if (rendezvous.ThrowOnHandle)
        {
            throw new InvalidOperationException("Playground: handler deliberately threw.");
        }

        return Task.CompletedTask;
    }
}
