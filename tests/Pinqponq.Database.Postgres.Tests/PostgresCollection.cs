using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Postgres.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresCollectionFixture>
{
    public const string Name = "postgres";
}

public sealed class PostgresCollectionFixture : PostgresFixture, IAsyncLifetime;
