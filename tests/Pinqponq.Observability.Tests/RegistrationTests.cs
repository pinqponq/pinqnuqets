using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pinqloq;
using Pinqponq.Observability.DependencyInjection;
using Xunit;

namespace Pinqponq.Observability.Tests;

public sealed class RegistrationTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Observability:HttpCollectionName"] = "app_http",
        ["Observability:JobsCollectionName"] = "app_jobs",
        ["Pinqloq:SecretKey"] = "pinqloq-secret",
    };

    private static async Task<IHost> StartAsync(
        Dictionary<string, string?> settings,
        Action<ObservabilityOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddPinqponqObservability(configuration, configure);
                });
                web.Configure(_ => { });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Registration_binds_the_section_and_configures_pinqloq()
    {
        using var host = await StartAsync(
            ValidSettings,
            configure: observabilityOptions => observabilityOptions.RedactedKeys.Add("X-Signature"));

        var observabilityOptions = host.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        var pinqloqOptions = host.Services.GetRequiredService<IOptions<PinqloqOptions>>().Value;
        Assert.Equal("app_http", observabilityOptions.HttpCollectionName);
        Assert.Equal(["X-Signature"], observabilityOptions.RedactedKeys);
        Assert.Null(observabilityOptions.DeviceIdentifierHeader);
        Assert.Empty(observabilityOptions.LoggedContextItems);
        Assert.Equal("app_jobs", pinqloqOptions.ApiLogsCollectionName);
        Assert.Equal("pinqloq-secret", pinqloqOptions.SecretKey);
        Assert.Equal(Environment.MachineName, pinqloqOptions.DeviceIdentifier);
        Assert.IsType<LogService>(host.Services.GetRequiredService<ILogService>());
    }

    [Fact]
    public async Task Missing_secret_key_fails_the_startup_in_the_pinqloq_client()
    {
        var settingsWithoutSecret = ValidSettings.Where(setting => setting.Key != "Pinqloq:SecretKey").ToDictionary();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(settingsWithoutSecret));

        Assert.Contains("SecretKey", exception.Message);
    }

    [Fact]
    public async Task Missing_collection_names_fail_the_startup()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            StartAsync(new Dictionary<string, string?> { ["Observability:MaxLoggedBodyBytes"] = "0", ["Pinqloq:SecretKey"] = "pinqloq-secret" }));

        Assert.Contains("Observability:HttpCollectionName is required.", exception.Failures);
        Assert.Contains("Observability:JobsCollectionName is required.", exception.Failures);
        Assert.Contains("Observability:MaxLoggedBodyBytes must be greater than 0.", exception.Failures);
    }
}
