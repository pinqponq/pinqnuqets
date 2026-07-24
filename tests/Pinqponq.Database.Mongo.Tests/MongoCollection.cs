using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Mongo.Tests;

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoCollectionFixture>
{
    public const string Name = "mongo";
}

public sealed class MongoCollectionFixture : MongoFixture, IAsyncLifetime;
