using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Mail.Tests;

[CollectionDefinition(Name)]
public sealed class MailHogCollection : ICollectionFixture<MailHogCollectionFixture>
{
    public const string Name = "mailhog";
}

public sealed class MailHogCollectionFixture : MailHogFixture, IAsyncLifetime;
