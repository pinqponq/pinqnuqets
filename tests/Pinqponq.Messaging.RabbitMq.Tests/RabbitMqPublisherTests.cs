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
            // Allow the hosted consumer to finish BasicConsume.
            await Task.Delay(1500);

            var publisher = host.Services.GetRequiredService<IMessagePublisher>();
            await publisher.PublishAsync("", queue, "from-consumer-test");

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
}
