using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Hosting.Maintenance.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Maintenance.Tests;

public sealed class MaintenanceModeMiddlewareTests
{
    private static Dictionary<string, string?> CreateEnabledSettings(params (string Key, string? Value)[] extraSettings)
    {
        var settings = new Dictionary<string, string?> { ["MaintenanceMode:Enabled"] = "true" };
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        return settings;
    }

    [Fact]
    public async Task Request_passes_through_when_maintenance_mode_is_off()
    {
        await using var host = await MaintenanceModeHost.Start(new Dictionary<string, string?>());

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MaintenanceModeHost.EndpointBody, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(host.LoggerProvider.Records, record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Request_gets_503_with_the_configured_code_and_the_default_message_when_maintenance_mode_is_on()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings());

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Equal(MaintenanceModeEvents.RequestBlockedByMaintenance, Assert.Single(response.Headers.GetValues(MaintenanceModeHost.ErrorEventNameHeader)));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(503, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(MaintenanceModeHost.ResponseCode, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("The service is temporarily unavailable because of maintenance.", body.RootElement.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Request_gets_the_configured_status_code_response_code_and_message()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(
            ("MaintenanceMode:StatusCode", "502"),
            ("MaintenanceMode:ResponseCode", "planned_downtime"),
            ("MaintenanceMode:Message", "Back at noon")));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(502, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("planned_downtime", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Back at noon", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Empty_configured_message_is_written_as_it_is()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(("MaintenanceMode:Message", "")));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Blocked_request_is_logged_once_as_a_warning_with_method_and_path()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings());

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        var warning = Assert.Single(host.LoggerProvider.Records, record => record.Level == LogLevel.Warning);
        Assert.Equal($"Maintenance mode blocked GET {MaintenanceModeHost.ApiPath}.", warning.Message);
    }

    [Fact]
    public async Task Configuration_reload_turns_maintenance_mode_on_and_off_without_a_restart()
    {
        await using var host = await MaintenanceModeHost.Start(new Dictionary<string, string?> { ["MaintenanceMode:Enabled"] = "false" });

        using var beforeResponse = await host.Send(MaintenanceModeHost.ApiPath);
        host.ReloadSetting("MaintenanceMode:Enabled", "true");
        using var duringResponse = await host.Send(MaintenanceModeHost.ApiPath);
        host.ReloadSetting("MaintenanceMode:Enabled", "false");
        using var afterResponse = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, duringResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
    }

    [Fact]
    public async Task Invalid_reload_fails_every_request_until_the_configuration_is_valid_again()
    {
        await using var host = await MaintenanceModeHost.Start(new Dictionary<string, string?>());

        // The options monitor builds the new options inside the reload callback, so the in-memory reload throws here.
        // A file reload runs that callback on a background task instead.
        var reloadException = Record.Exception(() => host.ReloadSetting("MaintenanceMode:StatusCode", "200"));
        var apiException = await Assert.ThrowsAsync<OptionsValidationException>(() => host.Send(MaintenanceModeHost.ApiPath));
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.Send(MaintenanceModeHost.HealthPath));
        host.ReloadSetting("MaintenanceMode:StatusCode", "503");
        using var validResponse = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.NotNull(reloadException);
        Assert.Contains("StatusCode must be from 400 to 599.", apiException.Failures);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH/ready")]
    public async Task Default_exempt_path_passes_while_maintenance_mode_is_on(string path)
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings());

        using var response = await host.Send(path);

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Path_that_only_starts_with_the_same_letters_as_an_exempt_prefix_is_blocked()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings());

        using var response = await host.Send("/healthz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Configured_exempt_path_is_added_to_the_default_one()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(("MaintenanceMode:ExemptPathPrefixes:0", "/api")));

        using var apiResponse = await host.Send(MaintenanceModeHost.ApiPath);
        using var healthResponse = await host.Send(MaintenanceModeHost.HealthPath);

        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    [Fact]
    public async Task Default_exempt_path_is_blocked_when_the_list_is_cleared_in_code()
    {
        await using var host = await MaintenanceModeHost.Start(
            CreateEnabledSettings(),
            (services, configuration) => services.AddPinqponqMaintenanceMode(configuration, options => options.ExemptPathPrefixes.Clear()));

        using var response = await host.Send(MaintenanceModeHost.HealthPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Retry_after_header_is_sent_when_configured()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(("MaintenanceMode:RetryAfterSeconds", "120")));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal("120", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    [Fact]
    public async Task Message_comes_from_the_catalog_when_error_localization_is_on()
    {
        await using var host = await MaintenanceModeHost.Start(
            CreateEnabledSettings(("MaintenanceMode:ResponseCode", CommonErrorCodes.TooManyRequests)),
            (services, configuration) =>
            {
                services.AddPinqponqErrorLocalization(options => options.CatalogDirectory = null);
                services.AddPinqponqMaintenanceMode(configuration);
            });

        using var response = await host.Send(MaintenanceModeHost.ApiPath, language: "tr-TR");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Çok fazla istek gönderildi. Daha sonra tekrar deneyin.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Configured_message_is_written_when_localization_is_on_and_localize_message_is_off()
    {
        await using var host = await MaintenanceModeHost.Start(
            CreateEnabledSettings(
                ("MaintenanceMode:ResponseCode", CommonErrorCodes.TooManyRequests),
                ("MaintenanceMode:Message", "Back at noon"),
                ("MaintenanceMode:LocalizeMessage", "false")),
            (services, configuration) =>
            {
                services.AddPinqponqErrorLocalization(options => options.CatalogDirectory = null);
                services.AddPinqponqMaintenanceMode(configuration);
            });

        using var response = await host.Send(MaintenanceModeHost.ApiPath, language: "tr-TR");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Back at noon", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(MaintenanceModeEvents.RequestBlockedByMaintenance, Assert.Single(response.Headers.GetValues(MaintenanceModeHost.ErrorEventNameHeader)));
    }

    [Fact]
    public async Task Configured_event_name_describes_the_blocked_request()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(("MaintenanceMode:EventName", "request_blocked_planned_downtime")));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal("request_blocked_planned_downtime", Assert.Single(response.Headers.GetValues(MaintenanceModeHost.ErrorEventNameHeader)));
    }

    [Fact]
    public async Task Configured_event_name_is_used_when_localize_message_is_off()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(
            ("MaintenanceMode:EventName", "request_blocked_planned_downtime"),
            ("MaintenanceMode:LocalizeMessage", "false")));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("request_blocked_planned_downtime", Assert.Single(response.Headers.GetValues(MaintenanceModeHost.ErrorEventNameHeader)));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("The service is temporarily unavailable because of maintenance.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Null_configured_message_keeps_the_default_message()
    {
        await using var host = await MaintenanceModeHost.Start(CreateEnabledSettings(("MaintenanceMode:Message", null)));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("The service is temporarily unavailable because of maintenance.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Configure_callback_runs_after_binding()
    {
        await using var host = await MaintenanceModeHost.Start(
            new Dictionary<string, string?> { ["MaintenanceMode:Enabled"] = "false" },
            (services, configuration) => services.AddPinqponqMaintenanceMode(configuration, options => options.Enabled = true));

        using var response = await host.Send(MaintenanceModeHost.ApiPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
