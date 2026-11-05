using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pinqponq.Observability.ClientLogs.DependencyInjection;
using Xunit;

namespace Pinqponq.Observability.ClientLogs.Tests;

public sealed class ClientLogRegistrationTests
{
    [Fact]
    public async Task A_missing_collection_name_stops_the_host_at_startup()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            ClientLogTestHost.Start(new CapturingPinqloqLogger(), new Dictionary<string, string?> { ["ClientLogs:CollectionName"] = " " }));

        Assert.Contains("ClientLogOptions.CollectionName is required.", exception.Failures);
    }

    [Theory]
    [InlineData("MaxEntriesPerBatch")]
    [InlineData("MaxBodyBytes")]
    [InlineData("MaxFieldsPerEntry")]
    [InlineData("MaxValueLength")]
    public async Task A_limit_of_zero_stops_the_host_at_startup(string limitName)
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            ClientLogTestHost.Start(new CapturingPinqloqLogger(), new Dictionary<string, string?> { [$"ClientLogs:{limitName}"] = "0" }));

        Assert.Equal([$"ClientLogOptions.{limitName} must be greater than 0 when it is set."], exception.Failures);
    }

    [Fact]
    public async Task A_body_limit_at_the_largest_array_length_stops_the_host_at_startup()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            ClientLogTestHost.Start(new CapturingPinqloqLogger(), new Dictionary<string, string?> { ["ClientLogs:MaxBodyBytes"] = Array.MaxLength.ToString() }));

        Assert.Equal([$"ClientLogOptions.MaxBodyBytes must be less than {Array.MaxLength}."], exception.Failures);
    }

    [Fact]
    public async Task A_body_limit_below_the_largest_array_length_is_accepted()
    {
        using var host = await ClientLogTestHost.Start(
            new CapturingPinqloqLogger(),
            new Dictionary<string, string?> { ["ClientLogs:MaxBodyBytes"] = (Array.MaxLength - 1).ToString() });

        Assert.NotNull(host);
    }

    [Fact]
    public async Task An_empty_redacted_key_part_stops_the_host_at_startup()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            ClientLogTestHost.Start(new CapturingPinqloqLogger(), new Dictionary<string, string?> { ["ClientLogs:RedactedKeyParts:0"] = " " }));

        Assert.Equal(["ClientLogOptions.RedactedKeyParts must not contain an empty entry."], exception.Failures);
    }

    [Fact]
    public async Task The_configure_callback_runs_after_the_section_binding()
    {
        var pinqloqLogger = new CapturingPinqloqLogger();
        using var host = await ClientLogTestHost.Start(
            pinqloqLogger,
            configureServices: services => services.AddPinqponqClientLogs(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                clientLogOptions => clientLogOptions.CollectionName = "configured_collection"));

        await host.GetTestClient().PostAsync(ClientLogTestHost.EntryPath, ClientLogTestHost.Json("""{"event":"app_started"}"""));

        Assert.Equal("configured_collection", Assert.Single(pinqloqLogger.Entries).CollectionName);
    }

    [Fact]
    public async Task Mapping_the_endpoints_without_the_registration_names_the_missing_call()
    {
        using var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services => services.AddRouting());
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapPinqponqClientLogBatch(ClientLogTestHost.BatchPath));
                });
            })
            .Build();
        await host.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.GetTestClient().PostAsync(ClientLogTestHost.BatchPath, ClientLogTestHost.Json("""{"logs":[{"event":"one"}]}""")));

        Assert.Equal("The client log endpoints need AddPinqponqClientLogs in the service registration.", exception.Message);
    }

    [Theory]
    [InlineData(ClientLogRejectionReason.BodyTooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(ClientLogRejectionReason.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(ClientLogRejectionReason.TooManyEntries, StatusCodes.Status400BadRequest)]
    [InlineData(ClientLogRejectionReason.TooManyFields, StatusCodes.Status400BadRequest)]
    [InlineData(ClientLogRejectionReason.EventTooLong, StatusCodes.Status400BadRequest)]
    public void The_rejection_reason_picks_the_status_code(ClientLogRejectionReason reason, int expectedStatusCode)
    {
        var exception = new ClientLogRejectedException(reason, "refused");

        Assert.Equal(expectedStatusCode, exception.StatusCode);
        Assert.Equal(reason, exception.Reason);
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => ClientLogRegistrationExtensions.AddPinqponqClientLogs(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqClientLogs(null!));
        Assert.Throws<ArgumentNullException>(() => ClientLogRegistrationExtensions.MapPinqponqClientLogs(null!, "/logs"));
        Assert.Throws<ArgumentNullException>(() => ClientLogRegistrationExtensions.MapPinqponqClientLogBatch(null!, "/logs"));
    }
}
