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
        yield return QueueRequired();
        yield return PublishRetry();
    }

    private static Scenario RoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.roundtrip",
            PackageId = Package,
            Title = "Publish → consume turu",
            Summary = "Gerçek bir hosted consumer başlatır, mesaj yayınlar ve handler'a "
                      + "ulaşmasını bekler. Consumer'ın hazır olması BasicConsume tamamlanana "
                      + "kadar beklenerek anlaşılır — sabit bir gecikmeyle değil.",
            RequiredServices = [DevServiceIds.RabbitMq],
            Fields =
            [
                new ScenarioField("message", "Mesaj", ScenarioFieldKind.Text, "merhaba rabbit"),
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
                    consumer.EnableDeadLetter = false;
                });
            });

            await host.StartHostedServicesAsync(context.CancellationToken);
            context.Step("Hosted consumer başlatıldı", $"kuyruk {queue}");

            var ready = await WaitForConsumerAsync(host, queue, context);
            context.Require("Consumer kuyruğa bağlandı", ready);

            var message = context.Input.Text("message");
            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, queue, message, context.CancellationToken);
            context.Step("Mesaj yayınlandı");

            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var received = await rendezvous.Reader.ReadAsync(receiveTimeout.Token);

            context.Require("Handler mesajı aldı", received == message, received);
            context.Artifact("tur", new { kuyruk = queue, gonderilen = message, alinan = received });
        });

    private static Scenario DeadLetter() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.dead-letter",
            PackageId = Package,
            Title = "Handler hata verince dead-letter'a düşer",
            Summary = "Handler exception fırlatır; paket mesajı requeue etmeden nack'ler ve DLX "
                      + "onu {kuyruk}.dead kuyruğuna taşır. Paketin ürettiği hata logu da "
                      + "yapılandırılmış hâliyle doğrulanır.",
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
            context.Require("Consumer kuyruğa bağlandı", await WaitForConsumerAsync(host, queue, context));

            const string Payload = "bu mesaj işlenemeyecek";
            await host.GetRequiredService<IMessagePublisher>()
                .PublishAsync(string.Empty, queue, Payload, context.CancellationToken);
            context.Step("Mesaj yayınlandı", $"kuyruk {queue}");

            using (var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                receiveTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                var seen = await rendezvous.Reader.ReadAsync(receiveTimeout.Token);
                context.Step("Handler mesajı gördü ve hata fırlattı", seen);
            }

            var deadLetterQueue = $"{queue}.dead";
            var deadLettered = await WaitForDeadLetterAsync(host, deadLetterQueue, context);

            context.Require("Mesaj dead-letter kuyruğuna düştü", deadLettered is not null, deadLetterQueue);
            context.Require("Gövde korunmuş", deadLettered == Payload, deadLettered);

            var logEntry = await context.WaitForLogAsync(
                record => record.MessageTemplate?.Contains("dead-letter", StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(10));

            context.Require("Paket hata logunu üretti", logEntry is not null);
            context.Check(
                "Log kaydında yapılandırılmış Queue alanı var",
                logEntry!.State.TryGetValue("Queue", out var loggedQueue)
                && string.Equals(loggedQueue?.ToString(), queue, StringComparison.Ordinal),
                logEntry.State.TryGetValue("Queue", out var value) ? value?.ToString() : "(yok)");

            context.Artifact("log kaydı", new
            {
                level = logEntry.Level,
                category = logEntry.Category,
                messageTemplate = logEntry.MessageTemplate,
                state = logEntry.State,
                exception = logEntry.Exception?.Type,
            });
        });

    private static Scenario QueueRequired() => new(
        new ScenarioDescriptor
        {
            Id = "rabbit.queue-required",
            PackageId = Package,
            Title = "Kuyruk adı boşsa kayıt anında hata verir",
            Summary = "AddRabbitMqConsumer, kuyruk adını çalışma anında değil kayıt anında "
                      + "doğrular. Broker gerekmez — hata DI kurulumunda oluşur.",
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

            context.Require("Kayıt anında InvalidOperationException", thrown is not null);
            context.Check(
                "Hata Queue alanını söylüyor",
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
            Title = "Broker kapalıyken publish tekrar dener",
            Summary = "Ulaşılamayan bir brokera yayın yapılır. PublishRetryCount arttıkça geçen "
                      + "süre belirgin biçimde uzar; sonunda hata çağırana yükselir.",
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
            context.Require("Denemesiz yayın hata verdi", withoutRetry.ExceptionType is not null);

            var retryCount = context.Input.Int("publishRetryCount");
            var withRetry = await MeasurePublishFailureAsync(context, retryCount);
            context.Step($"PublishRetryCount={retryCount} → {withRetry.ElapsedMs} ms", withRetry.ExceptionType);
            context.Require("Denemeli yayın da hata verdi", withRetry.ExceptionType is not null);

            context.Require(
                "Tekrar denemeler ölçülebilir ek süre getirdi",
                withRetry.ElapsedMs > withoutRetry.ElapsedMs,
                $"{withoutRetry.ElapsedMs} ms → {withRetry.ElapsedMs} ms");

            context.Artifact("ölçüm", new
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
            throw new InvalidOperationException("Playground: handler bilerek hata verdi.");
        }

        return Task.CompletedTask;
    }
}
