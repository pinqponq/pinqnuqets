using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class MaintenanceAndCancellationTests
{
    [Fact]
    public async Task Maintenance_mode_answers_503_without_running_the_pipeline()
    {
        var harness = new ErrorResponseHarness();
        harness.MaintenanceMode.Enabled = true;
        harness.MaintenanceMode.Message = "Back at 10:00";
        harness.Options.IncludeTraceId = false;
        var isPipelineRun = false;

        var httpContext = await harness.InvokeAsync(_ =>
        {
            isPipelineRun = true;
            return Task.CompletedTask;
        });

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.False(isPipelineRun);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.Equal(CommonErrorCodes.SystemMaintenance, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Back at 10:00", body.RootElement.GetProperty("message").GetString());
        Assert.False(body.RootElement.TryGetProperty("traceId", out _));
        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(ErrorHandlingEvents.RequestBlockedMaintenanceMode, errorFeature.EventName);
        Assert.Equal(503, errorFeature.Detail["statusCode"]);
        Assert.False(errorFeature.Detail.ContainsKey("language"));
        Assert.Equal(LogLevel.Warning, Assert.Single(harness.MaintenanceLogger.Records).Level);
    }

    [Fact]
    public async Task Maintenance_mode_without_a_message_uses_the_default_text()
    {
        var harness = new ErrorResponseHarness();
        harness.MaintenanceMode.Enabled = true;

        var httpContext = await harness.InvokeAsync(_ => Task.CompletedTask);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("System is under maintenance", body.RootElement.GetProperty("message").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Maintenance_mode_follows_a_configuration_reload()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MaintenanceMode:Enabled"] = "false" })
            .Build();
        using var host = await StartHostAsync(services =>
        {
            services.AddPinqponqErrorHandling();
            services.AddPinqponqMaintenanceMode(configuration.GetSection("MaintenanceMode"));
        });
        var client = host.GetTestClient();

        var beforeReload = await client.GetAsync("/");
        configuration["MaintenanceMode:Enabled"] = "true";
        configuration.Reload();
        var afterReload = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, beforeReload.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, afterReload.StatusCode);
        var body = await afterReload.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(CommonErrorCodes.SystemMaintenance, body!.ResponseCode);
    }

    [Fact]
    public async Task Any_cancellation_is_a_closed_client_request_when_configured()
    {
        var harness = new ErrorResponseHarness();
        harness.Options.TreatAnyCancellationAsClientClosed = true;

        var httpContext = await harness.InvokeAsync(_ => throw new TaskCanceledException());

        Assert.Equal(499, httpContext.Response.StatusCode);
        Assert.Equal(string.Empty, ErrorResponseHarness.ReadBodyText(httpContext));
        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(ErrorHandlingEvents.RequestCancelledByClient, errorFeature.EventName);
        Assert.Equal(499, errorFeature.Detail["statusCode"]);
        Assert.Equal(CommonErrorCodes.ApiClientClosedRequest, errorFeature.Detail["errorCode"]);
        Assert.Equal("Client closed the request.", errorFeature.Detail["errorMessage"]);
        Assert.Equal(LogLevel.Information, Assert.Single(harness.Logger.Records).Level);
    }

    [Fact]
    public async Task Cancellation_not_requested_by_the_client_stays_a_timeout_by_default()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(_ => throw new TaskCanceledException());

        Assert.Equal(StatusCodes.Status504GatewayTimeout, httpContext.Response.StatusCode);
        Assert.Equal("exception_TaskCanceledException", httpContext.Features.Get<IRequestErrorFeature>()?.EventName);
    }

    [Fact]
    public void Registration_helpers_enable_localization_and_bind_maintenance_mode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MaintenanceMode:Enabled"] = "true",
                ["MaintenanceMode:Message"] = "Later",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPinqponqErrorLocalization(localizationOptions => localizationOptions.LanguageHeader = "Language");
        services.AddPinqponqMaintenanceMode(configuration.GetSection("MaintenanceMode"));
        services.AddPinqponqExceptionResolver<NullExceptionResolver>();
        services.AddPinqponqExceptionResolver<NullExceptionResolver>();
        using var serviceProvider = services.BuildServiceProvider();

        var localizationOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorLocalizationOptions>>().Value;
        var maintenanceMode = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MaintenanceModeOptions>>().CurrentValue;
        Assert.True(localizationOptions.Enabled);
        Assert.Equal("Language", localizationOptions.LanguageHeader);
        Assert.True(maintenanceMode.Enabled);
        Assert.Equal("Later", maintenanceMode.Message);
        Assert.Single(serviceProvider.GetServices<IExceptionResolver>());
    }

    private static async Task<IHost> StartHostAsync(Action<IServiceCollection> configureServices)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging();
                    configureServices(services);
                });
                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.UsePinqponqMaintenanceMode();
                    app.Run(context => context.Response.WriteAsync("ok"));
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private sealed class NullExceptionResolver : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) => null;
    }
}
