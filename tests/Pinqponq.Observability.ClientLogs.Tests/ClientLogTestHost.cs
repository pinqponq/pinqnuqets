using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pinqloq;
using Pinqponq.ErrorHandling;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Observability.ClientLogs.DependencyInjection;

namespace Pinqponq.Observability.ClientLogs.Tests;

internal sealed class CapturingPinqloqLogger : IPinqloqLogger
{
    public List<PinqloqLogEntry> Entries { get; } = [];

    public bool Enqueue(PinqloqLogEntry entry, Action<PinqloqLogEntry>? onSent = null, Action<PinqloqLogEntry, PinqloqLogError>? onFailed = null)
    {
        Entries.Add(entry);
        return true;
    }

    public int Enqueue(IEnumerable<PinqloqLogEntry> entries, Action<PinqloqLogEntry>? onSent = null, Action<PinqloqLogEntry, PinqloqLogError>? onFailed = null) =>
        entries.Count(entry => Enqueue(entry, onSent, onFailed));
}

internal static class ClientLogTestHost
{
    public const string EntryPath = "/client-logs";
    public const string BatchPath = "/client-logs/batch";
    public const string CollectionName = "app_client";
    public const string DeviceHeader = "X-Device-Id";
    public const string RejectedResponseCodePrefix = "client_log_";

    public static async Task<IHost> Start(
        CapturingPinqloqLogger pinqloqLogger,
        IDictionary<string, string?>? clientLogSettings = null,
        Action<ObservabilityOptions>? configureObservability = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?> { ["ClientLogs:CollectionName"] = CollectionName };
        foreach (var setting in clientLogSettings ?? new Dictionary<string, string?>())
        {
            settings[setting.Key] = setting.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddPinqponqErrorHandling(errorHandlingOptions =>
                        errorHandlingOptions.StatusMappingResolver = exception => exception is ClientLogRejectedException rejected
                            ? new ExceptionStatusMapping(rejected.StatusCode, RejectedResponseCodePrefix + rejected.Reason)
                            : null);
                    services.AddSingleton<IPinqloqLogger>(pinqloqLogger);
                    services.Configure<ObservabilityOptions>(observabilityOptions =>
                    {
                        observabilityOptions.DeviceIdentifierHeader = DeviceHeader;
                        configureObservability?.Invoke(observabilityOptions);
                    });
                    services.AddPinqponqClientLogs(configuration.GetSection("ClientLogs"));
                    configureServices?.Invoke(services);
                });
                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPinqponqClientLogs(EntryPath);
                        endpoints.MapPinqponqClientLogBatch(BatchPath);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}

internal sealed class RecordingClientLogEnricher : IClientLogEnricher
{
    public int CallCount { get; private set; }

    public void Enrich(HttpContext httpContext, PinqloqLogEntry entry)
    {
        CallCount++;
        entry.Metadata ??= [];
        entry.Metadata["userId"] = httpContext.Request.Headers["X-Test-User"].ToString();
        entry.Metadata["enrichedSecret"] = "server-value";
    }
}
