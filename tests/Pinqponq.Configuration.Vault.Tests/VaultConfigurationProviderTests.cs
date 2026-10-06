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

        Assert.Equal("Host=db;Password=from-vault", GetValue(provider, "ConnectionStrings:PostgreSql"));
        Assert.Equal("rabbit-secret", GetValue(provider, "RabbitMQ:Password"));
        Assert.Equal("5672", GetValue(provider, "RabbitMQ:Port"));
        Assert.Equal("false", GetValue(provider, "RabbitMQ:Requeue"));
        Assert.Equal("first", GetValue(provider, "Hosts:0"));
        Assert.Equal("second", GetValue(provider, "Hosts:1"));
        Assert.Null(GetValue(provider, "Optional"));
    }

    [Fact]
    public void Load_DoesNotExposeVaultMetadataAsConfiguration()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        Assert.False(provider.TryGet("metadata:version", out _));
    }

    [Fact]
    public void Load_RequestsRecordFromKvVersion2DataPathWithToken()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler);

        provider.Load();

        var request = Assert.Single(handler.ReceivedRequests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://vault.test:8200/v1/apps/data/my-product/my-service", (request.RequestUri?.ToString()));
        Assert.Equal(CONFIGURED_TOKEN, Assert.Single(request.Headers.GetValues("X-Vault-Token")));
        Assert.False(request.Headers.Contains("X-Vault-Namespace"));
    }

    [Fact]
    public void Load_WhenNamespaceIsSet_SendsNamespaceHeader()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.OK, RECORD_RESPONSE);
        var provider = CreateProvider(handler, vaultOptions => vaultOptions.Namespace = "team-a");

        provider.Load();

        Assert.Equal("team-a", Assert.Single(handler.ReceivedRequests[0].Headers.GetValues("X-Vault-Namespace")));
    }

    [Fact]
    public void Load_WhenRecordDoesNotExist_ThrowsWithoutRetrying()
    {
        var handler = new StubVaultHandler().RespondWith(HttpStatusCode.NotFound);
        var provider = CreateProvider(handler);

        var load = provider.Load;

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains("apps/my-product/my-service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.ReceivedRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void Load_WhenTokenIsRejected_ThrowsWithoutRetryingOrLeakingTheToken(HttpStatusCode statusCode)
    {
        var handler = new StubVaultHandler().RespondWith(statusCode);
        var provider = CreateProvider(handler);

        var load = provider.Load;

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains("rejected the configured token", exception.Message);
        Assert.Contains("http://vault.test:8200", exception.Message);
        Assert.DoesNotContain(CONFIGURED_TOKEN, exception.Message);
        Assert.Single(handler.ReceivedRequests);
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

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains("'http://vault.test:8200/' rejected the token saved by the Vault CLI", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vault login -address=http://vault.test:8200/", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.Equal("rabbit-secret", GetValue(provider, "RabbitMQ:Password"));
        Assert.Equal(3, handler.ReceivedRequests.Count);
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

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains("unreachable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<HttpRequestException>(exception.InnerException);
        Assert.Equal(3, handler.ReceivedRequests.Count);
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

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains("missing or malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        var exception = Assert.ThrowsAny<VaultConfigurationException>(load);
        Assert.Contains($"'{settingName}'", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.ReceivedRequests);
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
        var isLoaded = provider.TryGet(configurationKey, out var configurationValue);
        Assert.True(isLoaded, $"configuration key '{configurationKey}' should be loaded");
        return configurationValue;
    }
}
