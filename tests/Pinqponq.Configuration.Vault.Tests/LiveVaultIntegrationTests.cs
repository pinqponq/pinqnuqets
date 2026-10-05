using FluentAssertions;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;

namespace Pinqponq.Configuration.Vault.Tests;

/// <summary>
/// Runs against a real Vault server when VAULT_TEST_ADDR and VAULT_TEST_TOKEN are set; otherwise every test
/// returns immediately. The token must be allowed to write and delete under VAULT_TEST_MOUNT (default: secret),
/// which must be a KV version 2 mount.
/// </summary>
public sealed class LiveVaultIntegrationTests
{
    private const string DEFAULT_MOUNT = "secret";
    private const string TOKEN_HEADER = "X-Vault-Token";

    private readonly ITestOutputHelper _output;
    private readonly string? _address;
    private readonly string? _token;
    private readonly string _mount;

    public LiveVaultIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _address = Environment.GetEnvironmentVariable("VAULT_TEST_ADDR");
        _token = Environment.GetEnvironmentVariable("VAULT_TEST_TOKEN");
        _mount = Environment.GetEnvironmentVariable("VAULT_TEST_MOUNT") ?? DEFAULT_MOUNT;
    }

    private bool IsConfigured => !string.IsNullOrEmpty(_address) && !string.IsNullOrEmpty(_token);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LoadsRecordWrittenToLiveVault_OverAppSettingsValues()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("VAULT_TEST_* not set; skipped.");
            return;
        }

        var recordPath = $"pinqponq-configuration-vault-tests/{Guid.NewGuid():N}";
        using var vaultClient = CreateVaultClient();
        var record = new
        {
            data = new
            {
                ConnectionStrings = new { PostgreSql = "Host=db;Password=from-vault" },
                RabbitMQ = new { Password = "rabbit-secret", Port = 5672 }
            }
        };

        using var writeResponse = await vaultClient.PostAsJsonAsync($"v1/{_mount}/data/{recordPath}", record);
        writeResponse.EnsureSuccessStatusCode();
        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Password"] = "from-appsettings",
                ["RabbitMQ:HostName"] = "rabbit-host"
            });

            configuration.AddPinqponqVault(vaultOptions =>
            {
                vaultOptions.Address = _address;
                vaultOptions.Token = _token;
                vaultOptions.Mount = _mount;
                vaultOptions.Path = recordPath;
            });

            configuration["ConnectionStrings:PostgreSql"].Should().Be("Host=db;Password=from-vault");
            configuration["RabbitMQ:Password"].Should().Be("rabbit-secret");
            configuration["RabbitMQ:Port"].Should().Be("5672");
            configuration["RabbitMQ:HostName"].Should().Be("rabbit-host");
        }
        finally
        {
            using var deleteResponse = await vaultClient.DeleteAsync($"v1/{_mount}/metadata/{recordPath}");
            _output.WriteLine($"Cleanup of '{_mount}/{recordPath}': {(int)deleteResponse.StatusCode}");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MissingRecordOnLiveVault_FailsWithoutRetrying()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("VAULT_TEST_* not set; skipped.");
            return;
        }

        var configuration = new ConfigurationManager();

        var addVault = () => configuration.AddPinqponqVault(vaultOptions =>
        {
            vaultOptions.Address = _address;
            vaultOptions.Token = _token;
            vaultOptions.Mount = _mount;
            vaultOptions.Path = $"pinqponq-configuration-vault-tests/missing-{Guid.NewGuid():N}";
        });

        addVault.Should().Throw<VaultConfigurationException>().WithMessage("*does not exist*");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void InvalidTokenOnLiveVault_IsReportedAsRejected()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("VAULT_TEST_* not set; skipped.");
            return;
        }

        var configuration = new ConfigurationManager();

        var addVault = () => configuration.AddPinqponqVault(vaultOptions =>
        {
            vaultOptions.Address = _address;
            vaultOptions.Token = "not-a-valid-token";
            vaultOptions.Mount = _mount;
            vaultOptions.Path = "pinqponq-configuration-vault-tests/any";
        });

        addVault.Should().Throw<VaultConfigurationException>().WithMessage("*rejected the configured token*");
    }

    private HttpClient CreateVaultClient()
    {
        var vaultClient = new HttpClient { BaseAddress = new Uri(_address!.TrimEnd('/') + "/") }; // Non-null when IsConfigured.
        vaultClient.DefaultRequestHeaders.Add(TOKEN_HEADER, _token);
        return vaultClient;
    }
}
