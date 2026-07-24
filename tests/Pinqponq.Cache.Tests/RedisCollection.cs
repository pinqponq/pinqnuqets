using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Cache.Tests;

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisCollectionFixture>
{
    public const string Name = "redis";
}

public sealed class RedisCollectionFixture : RedisFixture, IAsyncLifetime;
