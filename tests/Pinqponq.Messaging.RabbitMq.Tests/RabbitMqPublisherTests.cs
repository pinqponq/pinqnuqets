using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinqponq.Messaging.RabbitMq.DependencyInjection;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Messaging.RabbitMq.Tests;

[Collection(RabbitMqCollection.Name)]
public sealed class RabbitMqPublisherTests
{
    private readonly RabbitMqCollectionFixture _fixture;

    public RabbitMqPublisherTests(RabbitMqCollectionFixture fixture) => _fixture = fixture;

    private RabbitMqOptions Options() => new()
    {
        HostName = _fixture.HostName,
        Port = _fixture.Port,
        UserName = _fixture.UserName,
        Password = _fixture.Password,
        PublishRetryCount = 1,
        PublishRetryBaseDelay = TimeSpan.FromMilliseconds(50),
    };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Publish_delivers_message_to_queue()
    {
        var queue = $"q-{Guid.NewGuid():N}";
        await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options()));
        var publisher = new RabbitMqPublisher(connection, Microsoft.Extensions.Options.Options.Create(Options()));

        await using (var setup = await connection.CreateChannelAsync())
        {
            await setup.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        }

        await publisher.PublishAsync(exchange: "", routingKey: queue, "hello-rabbit");

        await using var channel = await connection.CreateChannelAsync();
        var result = await channel.BasicGetAsync(queue, autoAck: true);
        result.Should().NotBeNull();
        Encoding.UTF8.GetString(result!.Body.ToArray()).Should().Be("hello-rabbit");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Publish_null_message_throws()
    {
        await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options()));
        var publisher = new RabbitMqPublisher(connection, Microsoft.Extensions.Options.Options.Create(Options()));

        var act = () => publisher.PublishAsync("", "rk", (string)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Publish_to_missing_queue_throws()
    {
        var missing = $"missing-{Guid.NewGuid():N}";
        await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options()));
        var options = Options();
        options.PublishRetryCount = 1;
        var publisher = new RabbitMqPublisher(connection, Microsoft.Extensions.Options.Options.Create(options));

        var act = () => publisher.PublishAsync(exchange: "", routingKey: missing, "orphan");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failing_handler_with_dlx_off_eventually_drops_after_max_redelivery()
    {
        var queue = $"fail-{Guid.NewGuid():N}";
        var attempts = 0;

        await using (var bootstrap = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options())))
        await using (var channel = await bootstrap.CreateChannelAsync())
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        }

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(new AttemptCounter(() => Interlocked.Increment(ref attempts)));
                services.AddPinqponqRabbitMq(o =>
                {
                    o.HostName = _fixture.HostName;
                    o.Port = _fixture.Port;
                    o.UserName = _fixture.UserName;
                    o.Password = _fixture.Password;
                    o.PublishRetryCount = 1;
                });
                services.AddRabbitMqConsumer<FailingHandler>(o =>
                {
                    o.Queue = queue;
                    o.EnableDeadLetter = false;
                    o.MaxRedeliveryCount = 2;
                });
            })
            .Build();

        await host.StartAsync();
        try
        {
            var publisher = host.Services.GetRequiredService<IMessagePublisher>();
            await publisher.PublishAsync("", queue, "poison");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (Volatile.Read(ref attempts) < 3 && !cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token);
            }

            Volatile.Read(ref attempts).Should().BeGreaterThanOrEqualTo(3);

            await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options()));
            await using var channel = await connection.CreateChannelAsync();
            // Give the consumer a moment to drop the poison message.
            await Task.Delay(500);
            var leftover = await channel.BasicGetAsync(queue, autoAck: true);
            leftover.Should().BeNull("poison message should be dropped after MaxRedeliveryCount");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Consumer_handles_published_message()
    {
        var queue = $"cq-{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pre-declare the queue so early publishes are not dropped on the default exchange.
        await using (var bootstrap = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(Options())))
        await using (var channel = await bootstrap.CreateChannelAsync())
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        }

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(tcs);
                services.AddPinqponqRabbitMq(o =>
                {
                    o.HostName = _fixture.HostName;
                    o.Port = _fixture.Port;
                    o.UserName = _fixture.UserName;
                    o.Password = _fixture.Password;
                });
                services.AddRabbitMqConsumer<CapturingHandler>(o =>
                {
                    o.Queue = queue;
                    o.EnableDeadLetter = false;
                });
            })
            .Build();

        await host.StartAsync();
        try
        {
            var publisher = host.Services.GetRequiredService<IMessagePublisher>();

            // Retry publish until the consumer has finished BasicConsume (hosted lifetime).
            using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Exception? lastPublishError = null;
            while (!readyCts.IsCancellationRequested)
            {
                try
                {
                    await publisher.PublishAsync("", queue, "from-consumer-test", readyCts.Token);
                    lastPublishError = null;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastPublishError = ex;
                    await Task.Delay(100, readyCts.Token);
                }
            }

            lastPublishError.Should().BeNull("consumer should accept publishes within the readiness window");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var message = await tcs.Task.WaitAsync(cts.Token);
            message.Should().Be("from-consumer-test");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class CapturingHandler(TaskCompletionSource<string> tcs) : IMessageHandler
    {
        public Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            tcs.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    private sealed class AttemptCounter(Func<int> onAttempt)
    {
        public int Next() => onAttempt();
    }

    private sealed class FailingHandler(AttemptCounter counter) : IMessageHandler
    {
        public Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            counter.Next();
            throw new InvalidOperationException("handler failed");
        }
    }
}
