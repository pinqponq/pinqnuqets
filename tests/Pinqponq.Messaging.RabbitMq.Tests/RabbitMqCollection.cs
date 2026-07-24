using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Messaging.RabbitMq.Tests;

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqCollectionFixture>
{
    public const string Name = "rabbitmq";
}

public sealed class RabbitMqCollectionFixture : RabbitMqFixture, IAsyncLifetime;
