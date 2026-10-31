using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinqponq.Contracts;
using Pinqponq.Hosting.Maintenance.DependencyInjection;

namespace Pinqponq.Hosting.Maintenance.Tests;

/// <summary>
/// A test server with maintenance mode in front of two endpoints. The configuration is an in-memory source,
/// so a test can change a value and reload it while the host runs.
/// </summary>
internal sealed class MaintenanceModeHost : IAsyncDisposable
{
    public const string ApiPath = "/api/items";
    public const string HealthPath = "/health/ready";
    public const string EndpointBody = "ok";
    public const string ErrorEventNameHeader = "X-Test-Error-Event";

    private readonly IHost _host;
    private readonly IConfigurationRoot _configuration;

    private MaintenanceModeHost(IHost host, IConfigurationRoot configuration, CapturingLoggerProvider loggerProvider)
    {
        _host = host;
        _configuration = configuration;
        LoggerProvider = loggerProvider;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider LoggerProvider { get; }

    public static async Task<MaintenanceModeHost> Start(
        IDictionary<string, string?> settings,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var loggerProvider = new CapturingLoggerProvider();
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddProvider(loggerProvider));
                    services.AddRouting();
                    if (configureServices is null)
                    {
                        services.AddPinqponqMaintenanceMode(configuration);
                    }
                    else
                    {
                        configureServices(services, configuration);
                    }
                });
                web.Configure(app =>
                {
                    app.Use(CopyErrorEventNameToHeader);
                    app.UsePinqponqMaintenanceMode();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(ApiPath, () => EndpointBody);
                        endpoints.MapGet(HealthPath, () => EndpointBody);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new MaintenanceModeHost(host, configuration, loggerProvider);
    }

    public Task<HttpResponseMessage> Send(string path, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return Client.SendAsync(request);
    }

    public void ReloadSetting(string key, string value)
    {
        _configuration[key] = value;
        _configuration.Reload();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // The test client cannot read HttpContext.Features, so the event name is copied into a response header
    // before the response starts.
    private static Task CopyErrorEventNameToHeader(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            var errorFeature = context.Features.Get<IRequestErrorFeature>();
            if (errorFeature is not null)
            {
                context.Response.Headers[ErrorEventNameHeader] = errorFeature.EventName;
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
