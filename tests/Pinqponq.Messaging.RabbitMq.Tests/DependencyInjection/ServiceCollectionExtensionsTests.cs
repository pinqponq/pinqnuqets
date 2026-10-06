using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Messaging.RabbitMq.DependencyInjection;
using Xunit;

namespace Pinqponq.Messaging.RabbitMq.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqRabbitMq_registers_publisher()
    {
        var services = new ServiceCollection();
        services.AddPinqponqRabbitMq(o =>
        {
            o.HostName = "localhost";
            o.Port = 5672;
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IMessagePublisher));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqConnection));
    }

    [Fact]
    public void AddRabbitMqConsumer_requires_queue()
    {
        var services = new ServiceCollection();
        var act = () => services.AddRabbitMqConsumer<NoopHandler>(o => o.Queue = " ");
        Assert.ThrowsAny<InvalidOperationException>(act);
    }

    private sealed class NoopHandler : IMessageHandler
    {
        public Task HandleAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
