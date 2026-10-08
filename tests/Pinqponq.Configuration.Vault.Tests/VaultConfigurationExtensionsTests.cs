using Microsoft.Extensions.Configuration;
using System.Net;
using Xunit;

namespace Pinqponq.Configuration.Vault.Tests;

public sealed class VaultConfigurationExtensionsTests
{
    [Theory]
    [InlineData("Vault:Mount")]
    [InlineData("Vault:Path")]
    public void AddPinqponqVault_WhenLocationSettingIsMissing_ThrowsNamingTheSetting(string missingSettingKey)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Vault:Address"] = "http://vault.test:8200",
            ["Vault:Token"] = "test-token",
            ["Vault:Mount"] = "apps",
            ["Vault:Path"] = "my-product/my-service"
        };
        settings.Remove(missingSettingKey);
        var configuration = CreateConfiguration(settings);

        var addVault = () => configuration.AddPinqponqVault();

        var exception = Assert.ThrowsAny<VaultConfigurationException>(addVault);
        Assert.StartsWith(missingSettingKey, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(configuration.Sources, source => source is VaultConfigurationSource);
    }

    [Fact]
    public void AddPinqponqVault_ReadsTheNamedSection()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Secrets:Address"] = "http://vault.test:8200",
            ["Secrets:Mount"] = "apps"
        });

        var addVault = () => configuration.AddPinqponqVault(sectionName: "Secrets");

        var exception = Assert.ThrowsAny<VaultConfigurationException>(addVault);
        Assert.StartsWith("Secrets:Path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("ten")]
    public void AddPinqponqVault_WhenRequestTimeoutIsInvalid_Throws(string requestTimeoutSetting)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Vault:Address"] = "http://vault.test:8200",
            ["Vault:Mount"] = "apps",
            ["Vault:Path"] = "my-product/my-service",
            ["Vault:RequestTimeoutSeconds"] = requestTimeoutSetting
        });

        var addVault = () => configuration.AddPinqponqVault();

        var exception = Assert.ThrowsAny<VaultConfigurationException>(addVault);
        Assert.StartsWith("Vault:RequestTimeoutSeconds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPinqponqVault_WithOptionsCallback_OverridesEarlierSourcesAndKeepsTheRest()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["RabbitMQ:Password"] = "from-appsettings",
            ["RabbitMQ:HostName"] = "rabbit-host"
        });
        var handler = new StubVaultHandler().RespondWith(
            HttpStatusCode.OK,
            """{ "data": { "data": { "RabbitMQ": { "Password": "from-vault" } } } }""");

        configuration.AddPinqponqVault(vaultOptions =>
        {
            vaultOptions.Address = "http://vault.test:8200";
            vaultOptions.Token = "test-token";
            vaultOptions.Mount = "apps";
            vaultOptions.Path = "my-product/my-service";
            vaultOptions.HttpMessageHandler = handler;
        });

        Assert.Equal("from-vault", configuration["RabbitMQ:Password"]);
        Assert.Equal("rabbit-host", configuration["RabbitMQ:HostName"]);
    }

    private static ConfigurationManager CreateConfiguration(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(settings);
        return configuration;
    }
}
