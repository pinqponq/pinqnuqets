using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Database.Mssql.Tests;

[CollectionDefinition(Name)]
public sealed class MsSqlCollection : ICollectionFixture<MsSqlCollectionFixture>
{
    public const string Name = "mssql";
}

public sealed class MsSqlCollectionFixture : MsSqlFixture, IAsyncLifetime;
