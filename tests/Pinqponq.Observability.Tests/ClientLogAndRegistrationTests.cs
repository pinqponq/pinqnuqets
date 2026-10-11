using System.Net;
using System.Net.Http.Json;
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

public sealed class ClientLogAndRegistrationTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Observability:HttpCollectionName"] = "app_http",
        ["Observability:JobsCollectionName"] = "app_jobs",
        ["Observability:ClientCollectionName"] = "app_client",
        ["Pinqloq:SecretKey"] = "pinqloq-secret",
    };

    private static async Task<IHost> StartAsync(
        Dictionary<string, string?> settings,
        CapturingPinqloqLogger? pinqloqLogger = null,
        Action<ObservabilityOptions>? configure = null,
        string? clientLogRoute = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddPinqponqObservability(configuration, configure);
                    if (pinqloqLogger is not null)
                    {
                        services.AddSingleton<IPinqloqLogger>(pinqloqLogger);
                    }
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        if (clientLogRoute is null)
                        {
                            endpoints.MapPinqponqClientLogs();
                        }
                        else
                        {
                            endpoints.MapPinqponqClientLogs(clientLogRoute);
                        }
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Client_log_is_relayed_to_the_client_collection()
    {
        var pinqloqLogger = new CapturingPinqloqLogger();
        using var host = await StartAsync(ValidSettings, pinqloqLogger);
        var date = new DateTime(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client-logs")
        {
            Content = JsonContent.Create(new
            {
                @event = "checkout_opened",
                logLevel = "warn",
                metadata = new Dictionary<string, string> { ["screen"] = "cart" },
                detail = new Dictionary<string, string> { ["items"] = "3" },
                date,
            }),
        };
        request.Headers.Add("Identifier", "device-1");
        request.Headers.Add(PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName, "corr-1");

        var response = await host.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(pinqloqLogger.Entries);
        Assert.Equal("checkout_opened", entry.Event);
        Assert.Equal(PinqloqLogLevel.Warning, entry.LogLevel);
        Assert.Equal(PinqloqLogSourceType.Device, entry.LogSourceType);
        Assert.Equal("app_client", entry.CollectionName);
        Assert.Equal("device-1", entry.DeviceIdentifier);
        Assert.Equal("corr-1", entry.CorrelationId);
        Assert.Equal("cart", entry.Metadata!["screen"]);
        Assert.Equal("3", entry.Detail!["items"]);
        Assert.Equal(new DateTimeOffset(date), entry.Date);
    }

    [Fact]
    public async Task Client_log_on_a_custom_route_falls_back_to_the_trace_identifier()
    {
        var pinqloqLogger = new CapturingPinqloqLogger();
        using var host = await StartAsync(ValidSettings, pinqloqLogger, clientLogRoute: "/api/app/client-logs");

        var response = await host.GetTestClient().PostAsJsonAsync("/api/app/client-logs", new { @event = "app_started" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(pinqloqLogger.Entries);
        Assert.Equal(PinqloqLogLevel.Information, entry.LogLevel);
        Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
    }

    [Theory]
    [InlineData("debug", PinqloqLogLevel.Debug)]
    [InlineData("Information", PinqloqLogLevel.Information)]
    [InlineData("WARNING", PinqloqLogLevel.Warning)]
    [InlineData("warn", PinqloqLogLevel.Warning)]
    [InlineData("error", PinqloqLogLevel.Error)]
    [InlineData("fatal", PinqloqLogLevel.Fatal)]
    [InlineData("verbose", PinqloqLogLevel.Information)]
    public void Client_log_level_is_parsed_case_insensitively(string logLevel, PinqloqLogLevel expectedLevel)
    {
        Assert.Equal(expectedLevel, ClientLogEndpoint.ParseLogLevel(logLevel));
    }

    [Fact]
    public async Task Registration_binds_the_section_and_configures_pinqloq()
    {
        using var host = await StartAsync(
            ValidSettings,
            configure: observabilityOptions => observabilityOptions.DeviceUidExemptPathSegments.Add("/webhooks/billing"));

        var observabilityOptions = host.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        var pinqloqOptions = host.Services.GetRequiredService<IOptions<PinqloqOptions>>().Value;
        Assert.Equal("app_http", observabilityOptions.HttpCollectionName);
        Assert.Equal(["/webhooks/billing"], observabilityOptions.DeviceUidExemptPathSegments);
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
        Assert.Contains("Observability:ClientCollectionName is required.", exception.Failures);
        Assert.Contains("Observability:MaxLoggedBodyBytes must be greater than 0.", exception.Failures);
    }
}
