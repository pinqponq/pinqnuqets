using FluentAssertions;
using System.Net;
using Xunit;

namespace Pinqponq.Configuration.Vault.Tests;

public sealed class VaultConfigurationProviderTests
{
    private const string CONFIGURED_TOKEN = "configured-token";
    private const string RECORD_RESPONSE = """
        {
          "data": {
            "data": {
              "ConnectionStrings": { "PostgreSql": "Host=db;Password=from-vault" },
              "RabbitMQ": { "Password": "rabbit-secret", "Port": 5672, "Requeue": false },
              "Hosts": [ "first", "second" ],
              "Optional": null
            },
            "metadata": { "version": 3 }
          }
        }
        """;

    [Fact]
    public void Load_FlattensNestedRecordIntoConfigurationKeys()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        GetValue(provider, "ConnectionStrings:PostgreSql").Should().Be("Host=db;Password=from-vault");
        GetValue(provider, "RabbitMQ:Password").Should().Be("rabbit-secret");
        GetValue(provider, "RabbitMQ:Port").Should().Be("5672");
        GetValue(provider, "RabbitMQ:Requeue").Should().Be("false");
        GetValue(provider, "Hosts:0").Should().Be("first");
        GetValue(provider, "Hosts:1").Should().Be("second");
        GetValue(provider, "Optional").Should().BeNull();
    }

    [Fact]
    public void Load_DoesNotExposeVaultMetadataAsConfiguration()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        provider.TryGet("metadata:version", out _).Should().BeFalse();
    }

    [Fact]
    public void Load_RequestsRecordFromKvVersion2DataPathWithToken()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        var request = handler.ReceivedRequests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        (request.RequestUri?.ToString()).Should().Be("http://vault.test:8200/v1/apps/data/my-product/my-service");
        request.Headers.GetValues("X-Vault-Token").Should().Equal(CONFIGURED_TOKEN);
        request.Headers.Contains("X-Vault-Namespace").Should().BeFalse();
    }

    [Fact]
    public void Load_WhenNamespaceIsSet_SendsNamespaceHeader()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler, vaultOptions => vaultOptions.Namespace = "team-a");

        provider.Load();

        handler.ReceivedRequests[0].Headers.GetValues("X-Vault-Namespace").Should().Equal("team-a");
    }

    [Fact]
    public void Load_WhenRecordDoesNotExist_ThrowsWithoutRetrying()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.NotFound);
        var provider = CreateProvider(handler);

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>().WithMessage("*apps/my-product/my-service*does not exist*");
        handler.ReceivedRequests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void Load_WhenTokenIsRejected_ThrowsWithoutRetryingOrLeakingTheToken(HttpStatusCode statusCode)
    {
        var handler = new StubVaultHandler().RespondWith(statusCode);
        var provider = CreateProvider(handler);

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>()
            .Where(exception => exception.Message.Contains("rejected the configured token")
                && exception.Message.Contains("http://vault.test:8200")
                && !exception.Message.Contains(CONFIGURED_TOKEN));
        handler.ReceivedRequests.Should().ContainSingle();
    }

    [Fact]
    public void Load_WhenTokenFromCliFileIsRejected_TellsTheDeveloperToLogInAgain()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.Forbidden);
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "cli-token");
        var provider = CreateProvider(
            handler,
            vaultOptions => vaultOptions.Token = null,
            new VaultCredentialResolver(_ => null, userProfile.DirectoryPath));

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>()
            .WithMessage("*'http://vault.test:8200/' rejected the token saved by the Vault CLI*vault login -address=http://vault.test:8200/*");
    }

    [Fact]
    public void Load_WhenVaultIsTemporarilyUnavailable_RetriesAndLoads()
    {
        var handler = new StubVaultHandler()
            .FailWith(new HttpRequestException("connection refused"))
            .RespondWith(HttpStatusCode.ServiceUnavailable)
            .RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        GetValue(provider, "RabbitMQ:Password").Should().Be("rabbit-secret");
        handler.ReceivedRequests.Should().HaveCount(3);
    }

    [Fact]
    public void Load_WhenVaultStaysUnreachable_ThrowsAfterMaxAttempts()
    {
        var handler = new StubVaultHandler()
            .FailWith(new HttpRequestException("connection refused"))
            .FailWith(new HttpRequestException("connection refused"))
            .FailWith(new HttpRequestException("connection refused"));
        var provider = CreateProvider(handler);

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>()
            .WithMessage("*unreachable*")
            .WithInnerException<HttpRequestException>();
        handler.ReceivedRequests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("""{ "data": { "metadata": {} } }""")]
    [InlineData("""{ "data": { "data": "not-an-object" } }""")]
    [InlineData("""[]""")]
    public void Load_WhenResponseHasNoRecord_Throws(string responseBody)
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, responseBody);
        var provider = CreateProvider(handler);

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>().WithMessage("*missing or malformed*");
    }

    [Theory]
    [InlineData(null, "my-product/my-service", "Mount")]
    [InlineData("apps", " ", "Path")]
    public void Load_WhenLocationSettingIsMissing_ThrowsNamingTheSetting(string? mount, string? path, string settingName)
    {
        var handler = new StubVaultHandler();
        var provider = CreateProvider(handler, vaultOptions =>
        {
            vaultOptions.Mount = mount;
            vaultOptions.Path = path;
        });

        var load = provider.Load;

        load.Should().Throw<VaultConfigurationException>().WithMessage($"*'{settingName}'*");
        handler.ReceivedRequests.Should().BeEmpty();
    }

    private static VaultConfigurationProvider CreateProvider(
        StubVaultHandler handler,
        Action<VaultConfigurationOptions>? configureOptions = null,
        VaultCredentialResolver? credentialResolver = null)
    {
        var vaultOptions = new VaultConfigurationOptions
        {
            Address = "http://vault.test:8200/",
            Token = CONFIGURED_TOKEN,
            Mount = "/apps/",
            Path = "/my-product/my-service",
            RetryDelay = TimeSpan.Zero,
            HttpMessageHandler = handler
        };
        configureOptions?.Invoke(vaultOptions);

        var source = new VaultConfigurationSource
        {
            Options = vaultOptions,
            CredentialResolver = credentialResolver ?? new VaultCredentialResolver(_ => null, userProfileDirectory: null)
        };
        return new VaultConfigurationProvider(source);
    }

    private static string? GetValue(VaultConfigurationProvider provider, string configurationKey)
    {
        provider.TryGet(configurationKey, out var configurationValue)
            .Should().BeTrue($"configuration key '{configurationKey}' should be loaded");
        return configurationValue;
    }
}
