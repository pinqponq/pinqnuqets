using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pinqloq;
using Xunit;

namespace Pinqponq.Observability.ClientLogs.Tests;

public sealed class ClientLogEndpointTests
{
    private readonly CapturingPinqloqLogger _pinqloqLogger = new();

    private static async Task<HttpResponseMessage> Post(IHost host, string path, string json, Action<HttpRequestMessage>? configureRequest = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = ClientLogTestHost.Json(json) };
        configureRequest?.Invoke(request);
        return await host.GetTestClient().SendAsync(request);
    }

    private static async Task AssertRejected(HttpResponseMessage response, HttpStatusCode expectedStatus, ClientLogRejectionReason expectedReason)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ClientLogTestHost.RejectedResponseCodePrefix + expectedReason, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task A_single_entry_is_written_as_a_device_log_in_the_configured_collection()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        var response = await Post(host, ClientLogTestHost.EntryPath,
            """{"event":"app_started","logLevel":"WARN","metadata":{"appVersion":"1.0.3"},"detail":{"screen":"home"},"date":"2026-10-23T08:00:00Z"}""",
            request =>
            {
                request.Headers.Add(ClientLogTestHost.DeviceHeader, "device-1");
                request.Headers.Add(PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName, "correlation-1");
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("app_started", entry.Event);
        Assert.Equal(PinqloqLogLevel.Warning, entry.LogLevel);
        Assert.Equal(PinqloqLogSourceType.Device, entry.LogSourceType);
        Assert.Equal(ClientLogTestHost.CollectionName, entry.CollectionName);
        Assert.Equal("device-1", entry.DeviceIdentifier);
        Assert.Equal("correlation-1", entry.CorrelationId);
        Assert.Equal(new Dictionary<string, string> { ["appVersion"] = "1.0.3" }, entry.Metadata);
        Assert.Equal(new Dictionary<string, string> { ["screen"] = "home" }, entry.Detail);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero), entry.Date);
    }

    [Fact]
    public async Task Without_a_correlation_header_the_trace_identifier_is_the_correlation_id()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"app_started"}""");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
    }

    [Fact]
    public async Task Without_a_device_identifier_header_setting_the_record_has_an_empty_device_identifier()
    {
        using var host = await ClientLogTestHost.Start(
            _pinqloqLogger,
            configureObservability: observabilityOptions => observabilityOptions.DeviceIdentifierHeader = null);

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"app_started"}""",
            request => request.Headers.Add(ClientLogTestHost.DeviceHeader, "device-1"));

        Assert.Equal(string.Empty, Assert.Single(_pinqloqLogger.Entries).DeviceIdentifier);
    }

    [Fact]
    public async Task Missing_fields_stay_empty_and_null_values_become_empty_strings()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"app_started","metadata":{"key":null}}""");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal(string.Empty, entry.Metadata!["key"]);
        Assert.Null(entry.Detail);
        Assert.Null(entry.Date);
        Assert.Equal(PinqloqLogLevel.Information, entry.LogLevel);
    }

    [Theory]
    [InlineData("DEBUG", PinqloqLogLevel.Debug)]
    [InlineData("debug", PinqloqLogLevel.Debug)]
    [InlineData("WARNING", PinqloqLogLevel.Warning)]
    [InlineData("Warn", PinqloqLogLevel.Warning)]
    [InlineData("ERROR", PinqloqLogLevel.Error)]
    [InlineData("Fatal", PinqloqLogLevel.Fatal)]
    [InlineData("Information", PinqloqLogLevel.Information)]
    [InlineData("anything else", PinqloqLogLevel.Information)]
    public async Task The_log_level_name_picks_the_record_level(string logLevel, PinqloqLogLevel expectedLevel)
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        await Post(host, ClientLogTestHost.BatchPath, $$"""{"logs":[{"event":"something_happened","logLevel":"{{logLevel}}"}]}""");

        Assert.Equal(expectedLevel, Assert.Single(_pinqloqLogger.Entries).LogLevel);
    }

    [Fact]
    public async Task A_batch_writes_every_entry_in_order_and_ignores_the_client_collection_name()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        var response = await Post(host, ClientLogTestHost.BatchPath,
            """{"collectionName":"chosen_by_client","logs":[{"event":"first"},{"event":"second","logLevel":"ERROR"}]}""",
            request => request.Headers.Add(ClientLogTestHost.DeviceHeader, "device-1"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["first", "second"], _pinqloqLogger.Entries.Select(entry => entry.Event));
        Assert.All(_pinqloqLogger.Entries, entry =>
        {
            Assert.Equal(ClientLogTestHost.CollectionName, entry.CollectionName);
            Assert.Equal("device-1", entry.DeviceIdentifier);
            Assert.Equal(PinqloqLogSourceType.Device, entry.LogSourceType);
        });
    }

    [Theory]
    [InlineData("""{"logs":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"logs":[null]}""")]
    [InlineData("""{"logs":[{"event":"fine"},{"event":"   "}]}""")]
    [InlineData("""{"logs":[{"event":"fine"},{"logLevel":"ERROR"}]}""")]
    [InlineData("""{"logs":[{"event":"fine"}""")]
    [InlineData("""{"logs":[{"event":"fine","metadata":{"count":1}}]}""")]
    [InlineData("")]
    public async Task An_invalid_batch_is_refused_and_writes_nothing(string json)
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        var response = await Post(host, ClientLogTestHost.BatchPath, json);

        await AssertRejected(response, HttpStatusCode.BadRequest, ClientLogRejectionReason.InvalidRequest);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Theory]
    [InlineData("""{"event":""}""")]
    [InlineData("""{"logLevel":"ERROR"}""")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task An_invalid_single_entry_is_refused(string json)
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        var response = await Post(host, ClientLogTestHost.EntryPath, json);

        await AssertRejected(response, HttpStatusCode.BadRequest, ClientLogRejectionReason.InvalidRequest);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task Without_limits_a_large_batch_with_long_values_is_written_unchanged()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);
        var longValue = new string('x', 10_000);
        var fields = "{" + string.Join(",", Enumerable.Range(0, 100).Select(index => $"\"field{index}\":\"{longValue}\"")) + "}";
        var longEvent = new string('e', 5000);
        var entries = string.Join(",", Enumerable.Range(0, 200).Select(_ => $$"""{"event":"{{longEvent}}","metadata":{{fields}}}"""));

        var response = await Post(host, ClientLogTestHost.BatchPath, $$"""{"logs":[{{entries}}]}""",
            request => request.Headers.Add(ClientLogTestHost.DeviceHeader, longValue));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(200, _pinqloqLogger.Entries.Count);
        Assert.Equal(longValue, _pinqloqLogger.Entries[0].Metadata!["field99"]);
        Assert.Equal(longValue, _pinqloqLogger.Entries[0].DeviceIdentifier);
    }

    [Fact]
    public async Task A_batch_at_the_entry_limit_is_written()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxEntriesPerBatch"] = "2" });

        var response = await Post(host, ClientLogTestHost.BatchPath, """{"logs":[{"event":"one"},{"event":"two"}]}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, _pinqloqLogger.Entries.Count);
    }

    [Fact]
    public async Task A_batch_over_the_entry_limit_is_refused_and_writes_nothing()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxEntriesPerBatch"] = "2" });

        var response = await Post(host, ClientLogTestHost.BatchPath, """{"logs":[{"event":"one"},{"event":"two"},{"event":"three"}]}""");

        await AssertRejected(response, HttpStatusCode.BadRequest, ClientLogRejectionReason.TooManyEntries);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task Metadata_and_detail_fields_at_the_limit_are_written()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxFieldsPerEntry"] = "3" });

        var response = await Post(host, ClientLogTestHost.EntryPath, """{"event":"crowded","metadata":{"a":"1","b":"2"},"detail":{"c":"3"}}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(_pinqloqLogger.Entries);
    }

    [Theory]
    [InlineData("""{"event":"crowded","metadata":{"a":"1","b":"2"},"detail":{"c":"3","d":"4"}}""")]
    [InlineData("""{"event":"crowded","detail":{"a":"1","b":"2","c":"3","d":"4"}}""")]
    [InlineData("""{"event":"crowded","metadata":{"a":"1","b":"2","c":"3","d":"4"}}""")]
    public async Task An_entry_over_the_field_limit_refuses_the_whole_batch(string crowdedEntry)
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxFieldsPerEntry"] = "3" });

        var response = await Post(host, ClientLogTestHost.BatchPath, $$"""{"logs":[{"event":"fine"},{{crowdedEntry}}]}""");

        await AssertRejected(response, HttpStatusCode.BadRequest, ClientLogRejectionReason.TooManyFields);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task An_event_name_at_the_value_limit_is_written()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxValueLength"] = "5" });

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"abcde"}""");

        Assert.Equal("abcde", Assert.Single(_pinqloqLogger.Entries).Event);
    }

    [Fact]
    public async Task An_event_name_over_the_value_limit_refuses_the_whole_batch()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxValueLength"] = "5" });

        var response = await Post(host, ClientLogTestHost.BatchPath, """{"logs":[{"event":"fine"},{"event":"abcdef"}]}""");

        await AssertRejected(response, HttpStatusCode.BadRequest, ClientLogRejectionReason.EventTooLong);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task Values_and_the_device_identifier_over_the_value_limit_are_cut()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxValueLength"] = "5" });

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"cut","metadata":{"long":"abcdefgh","exact":"abcde"},"detail":{"long":"123456"}}""",
            request => request.Headers.Add(ClientLogTestHost.DeviceHeader, "device-identifier"));

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("abcde", entry.Metadata!["long"]);
        Assert.Equal("abcde", entry.Metadata["exact"]);
        Assert.Equal("12345", entry.Detail!["long"]);
        Assert.Equal("devic", entry.DeviceIdentifier);
    }

    [Fact]
    public async Task A_body_at_the_byte_limit_is_written()
    {
        const string json = """{"event":"fits"}""";
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxBodyBytes"] = json.Length.ToString() });

        var response = await Post(host, ClientLogTestHost.EntryPath, json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task A_body_over_the_byte_limit_is_refused_with_413()
    {
        const string json = """{"event":"too_long"}""";
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxBodyBytes"] = (json.Length - 1).ToString() });

        var response = await Post(host, ClientLogTestHost.BatchPath, $$"""{"logs":[{{json}}]}""");

        await AssertRejected(response, HttpStatusCode.RequestEntityTooLarge, ClientLogRejectionReason.BodyTooLarge);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task A_body_without_a_length_header_is_still_stopped_at_the_byte_limit()
    {
        const string json = """{"event":"too_long"}""";
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:MaxBodyBytes"] = (json.Length - 1).ToString() });
        using var request = new HttpRequestMessage(HttpMethod.Post, ClientLogTestHost.EntryPath)
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;

        var response = await host.GetTestClient().SendAsync(request);

        await AssertRejected(response, HttpStatusCode.RequestEntityTooLarge, ClientLogRejectionReason.BodyTooLarge);
        Assert.Empty(_pinqloqLogger.Entries);
    }

    [Fact]
    public async Task Keys_listed_in_the_observability_settings_are_redacted_in_any_case()
    {
        using var host = await ClientLogTestHost.Start(
            _pinqloqLogger,
            configureObservability: observabilityOptions => observabilityOptions.RedactedKeys = ["email"]);

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"form_sent","metadata":{"Email":"jane@example.com","emailVerified":"true"},"detail":{"EMAIL":"jane@example.com"}}""");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("***REDACTED***", entry.Metadata!["Email"]);
        Assert.Equal("true", entry.Metadata["emailVerified"]);
        Assert.Equal("***REDACTED***", entry.Detail!["EMAIL"]);
    }

    [Fact]
    public async Task Keys_containing_a_redacted_key_part_are_redacted()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger, new Dictionary<string, string?> { ["ClientLogs:RedactedKeyParts:0"] = "token" });

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"form_sent","metadata":{"refreshToken":"refresh-value","screen":"profile"}}""");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("***REDACTED***", entry.Metadata!["refreshToken"]);
        Assert.Equal("profile", entry.Metadata["screen"]);
    }

    [Fact]
    public async Task Without_redaction_settings_no_key_is_redacted()
    {
        using var host = await ClientLogTestHost.Start(_pinqloqLogger);

        await Post(host, ClientLogTestHost.EntryPath, """{"event":"form_sent","metadata":{"refreshToken":"refresh-value"}}""");

        Assert.Equal("refresh-value", Assert.Single(_pinqloqLogger.Entries).Metadata!["refreshToken"]);
    }

    [Fact]
    public async Task Every_enricher_changes_each_entry_and_its_values_are_not_redacted()
    {
        var firstEnricher = new RecordingClientLogEnricher();
        var secondEnricher = new RecordingClientLogEnricher();
        using var host = await ClientLogTestHost.Start(
            _pinqloqLogger,
            new Dictionary<string, string?> { ["ClientLogs:RedactedKeyParts:0"] = "secret" },
            configureServices: services =>
            {
                services.AddSingleton<IClientLogEnricher>(firstEnricher);
                services.AddSingleton<IClientLogEnricher>(secondEnricher);
            });

        await Post(host, ClientLogTestHost.BatchPath, """{"logs":[{"event":"one","metadata":{"userId":"99"}},{"event":"two"}]}""",
            request => request.Headers.Add("X-Test-User", "42"));

        Assert.Equal(2, firstEnricher.CallCount);
        Assert.Equal(2, secondEnricher.CallCount);
        Assert.All(_pinqloqLogger.Entries, entry =>
        {
            Assert.Equal("42", entry.Metadata!["userId"]);
            Assert.Equal("server-value", entry.Metadata["enrichedSecret"]);
        });
    }

    [Fact]
    public async Task A_refused_batch_does_not_reach_the_enricher()
    {
        var enricher = new RecordingClientLogEnricher();
        using var host = await ClientLogTestHost.Start(
            _pinqloqLogger,
            new Dictionary<string, string?> { ["ClientLogs:MaxEntriesPerBatch"] = "1" },
            configureServices: services => services.AddSingleton<IClientLogEnricher>(enricher));

        await Post(host, ClientLogTestHost.BatchPath, """{"logs":[{"event":"one"},{"event":"two"}]}""");

        Assert.Equal(0, enricher.CallCount);
        Assert.Empty(_pinqloqLogger.Entries);
    }
}
