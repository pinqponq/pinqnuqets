using FluentAssertions;
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

        addVault.Should().Throw<VaultConfigurationException>().WithMessage($"{missingSettingKey}*");
        configuration.Sources.Should().NotContain(source => source is VaultConfigurationSource);
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

        addVault.Should().Throw<VaultConfigurationException>().WithMessage("Secrets:Path*");
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

        addVault.Should().Throw<VaultConfigurationException>().WithMessage("Vault:RequestTimeoutSeconds*");
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

        configuration["RabbitMQ:Password"].Should().Be("from-vault");
        configuration["RabbitMQ:HostName"].Should().Be("rabbit-host");
    }

    private static ConfigurationManager CreateConfiguration(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(settings);
        return configuration;
    }
}
