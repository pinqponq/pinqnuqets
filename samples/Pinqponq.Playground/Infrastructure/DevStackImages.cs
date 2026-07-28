namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Container images the dev stack provisions.
/// </summary>
/// <remarks>
/// Kept identical to the integration-test fixtures in
/// <c>tests/Pinqponq.TestSupport/Fixtures/</c> so the playground and the test suite
/// exercise the packages against the same server versions. A sample must not take a
/// project reference on the test project, hence the duplication.
/// </remarks>
public static class DevStackImages
{
    public const string Postgres = "postgres:16-alpine";
    public const string Redis = "redis:7.4-alpine";
    public const string RabbitMq = "rabbitmq:3.13-alpine";
    public const string Mongo = "mongo:7.0";
    public const string MailHog = "mailhog/mailhog:v1.0.1";
    public const string MsSql = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";
}

/// <summary>Well-known dev-stack service identifiers.</summary>
public static class DevServiceIds
{
    public const string Postgres = "postgres";
    public const string Redis = "redis";
    public const string RabbitMq = "rabbitmq";
    public const string Mongo = "mongo";
    public const string MailHog = "mailhog";
    public const string MsSql = "mssql";
}
