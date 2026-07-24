using Testcontainers.RabbitMq;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>Shared RabbitMQ container for integration tests.</summary>
public class RabbitMqFixture 
{
    private const ushort AmqpPort = 5672;

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.13-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    /// <summary>Broker host mapped to localhost.</summary>
    public string HostName => _container.Hostname;

    /// <summary>Mapped AMQP port.</summary>
    public int Port => _container.GetMappedPublicPort(AmqpPort);

    /// <summary>Broker username.</summary>
    public string UserName => "guest";

    /// <summary>Broker password.</summary>
    public string Password => "guest";

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
