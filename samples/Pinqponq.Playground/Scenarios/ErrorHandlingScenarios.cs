using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Pinqponq.ErrorHandling;
using Pinqponq.ErrorHandling.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.ErrorHandling</c>.
/// </summary>
/// <remarks>
/// The middleware is constructed once when the pipeline is built and captures
/// <c>IOptions&lt;ErrorHandlingOptions&gt;.Value</c> at that moment, so a shared branch in the
/// console could never honour per-run option changes. Each run therefore stands up a
/// throwaway in-memory pipeline with <c>TestServer</c> — the same harness the package's own
/// tests use. A permanently mounted branch at <c>/sandbox/errors</c> stays available for
/// poking at the real wire format with curl.
/// </remarks>
public static class ErrorHandlingScenarios
{
    private const string Package = "Pinqponq.ErrorHandling";

    private static readonly IReadOnlyList<ErrorCase> Cases =
    [
        new("unauthorized", "UnauthorizedAccessException", 401, "unauthorized",
            () => throw new UnauthorizedAccessException("You are not authorized.")),
        new("not-found", "KeyNotFoundException", 404, "not_found",
            () => throw new KeyNotFoundException("Record not found.")),
        new("argument", "ArgumentException", 400, "bad_request",
            () => throw new ArgumentException("Invalid argument.")),
        new("format", "FormatException", 400, "bad_request",
            () => throw new FormatException("Malformed value.")),
        new("invalid-operation", "InvalidOperationException", 400, "bad_request",
            () => throw new InvalidOperationException("This operation cannot be performed right now.")),
        new("not-implemented", "NotImplementedException", 501, "not_implemented",
            () => throw new NotImplementedException("Not implemented yet.")),
        new("timeout", "TimeoutException", 504, "timeout",
            () => throw new TimeoutException("The operation timed out.")),
        new("custom-notfound", "WidgetNotFoundException (naming convention)", 404, "not_found",
            () => throw new WidgetNotFoundException("Widget not found.")),
        new("unhandled", "Exception", 500, "internal_error",
            () => throw new InvalidProgramException("Unexpected error.")),
    ];

    /// <summary>Case identifiers the permanently mounted sandbox branch understands.</summary>
    public static IReadOnlyList<string> CaseNames => [.. Cases.Select(item => item.Id)];

    /// <summary>Throws the exception a sandbox case stands for.</summary>
    public static void Throw(string caseId)
    {
        var match = Cases.FirstOrDefault(item => string.Equals(item.Id, caseId, StringComparison.Ordinal))
                    ?? throw new KeyNotFoundException($"Unknown case: {caseId}");

        match.Throw();
    }

    /// <summary>The expected mapping, so the console can render an expected/actual table.</summary>
    public static IEnumerable<object> Expectations() =>
        Cases.Select(item => new
        {
            id = item.Id,
            exception = item.ExceptionLabel,
            statusCode = item.ExpectedStatus,
            responseCode = item.ExpectedResponseCode,
        });

    public static IEnumerable<Scenario> Create()
    {
        yield return MappingMatrix();
        yield return LogShape();
        yield return Correlation();
    }

    private static Scenario MappingMatrix() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.mapping",
            PackageId = Package,
            Title = "Exception → HTTP status map",
            Summary = "Nine exception types are run through a real ASP.NET Core pipeline. Each row "
                      + "compares the expected and actual status code and responseCode.",
            TimeoutSeconds = 60,
        },
        async context =>
        {
            await using var pipeline = await BuildPipelineAsync(context, _ => { });
            var client = pipeline.Client;
            var rows = new List<object>();

            foreach (var item in Cases)
            {
                using var response = await client.GetAsync(new Uri($"/throw/{item.Id}", UriKind.Relative), context.CancellationToken);
                var body = await ReadErrorAsync(response, context);

                var ok = (int)response.StatusCode == item.ExpectedStatus
                         && body?.ResponseCode == item.ExpectedResponseCode;

                context.Check(
                    $"{item.ExceptionLabel} → {item.ExpectedStatus} {item.ExpectedResponseCode}",
                    ok,
                    $"got {(int)response.StatusCode} {body?.ResponseCode}");

                rows.Add(new
                {
                    exception = item.ExceptionLabel,
                    expectedStatus = item.ExpectedStatus,
                    actualStatus = (int)response.StatusCode,
                    expectedCode = item.ExpectedResponseCode,
                    actualCode = body?.ResponseCode,
                    message = body?.Message,
                });
            }

            context.Artifact("map", rows, "table");
        });

    private static Scenario LogShape() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.log-shape",
            PackageId = Package,
            Title = "Shape of the produced log record",
            Summary = "The package's claim is that it logs errors in the structured format Pinqloq "
                      + "expects. This scenario produces a single error and verifies the record's "
                      + "message template, field names, and level exactly.",
            Fields =
            [
                new ScenarioField("case", "Case", ScenarioFieldKind.Enum, "unhandled", null,
                    [.. Cases.Select(item => item.Id)]),
            ],
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var caseId = context.Input.Text("case");
            var expected = Cases.First(item => string.Equals(item.Id, caseId, StringComparison.Ordinal));

            await using var pipeline = await BuildPipelineAsync(context, _ => { });

            using var response = await pipeline.Client.GetAsync(
                new Uri($"/throw/{caseId}", UriKind.Relative),
                context.CancellationToken);

            context.Step($"Request sent, returned {(int)response.StatusCode}");

            var entry = await context.WaitForLogAsync(
                record => record.Category.Contains("ExceptionHandlingMiddleware", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));

            context.Require("Middleware produced a log record", entry is not null);

            const string ExpectedTemplate =
                "Request failed. {ResponseCode} {StatusCode} {Method} {Path} traceId={TraceId} correlationId={CorrelationId}";

            context.Require(
                "Message template matches the expected shape",
                entry!.MessageTemplate == ExpectedTemplate,
                entry.MessageTemplate);

            string[] expectedKeys = ["ResponseCode", "StatusCode", "Method", "Path", "TraceId", "CorrelationId"];
            var missing = expectedKeys.Where(key => !entry.State.ContainsKey(key)).ToArray();
            context.Require(
                "All structured fields are present",
                missing.Length == 0,
                missing.Length == 0 ? null : $"missing: {string.Join(", ", missing)}");

            var expectedLevel = expected.ExpectedStatus >= 500 ? "Error" : "Warning";
            context.Require(
                $"Level is {expectedLevel} (for {expected.ExpectedStatus})",
                entry.Level == expectedLevel,
                entry.Level);

            context.Require("Exception record is carried", entry.Exception is not null);
            context.Check(
                "ResponseCode field is correct",
                entry.State["ResponseCode"]?.ToString() == expected.ExpectedResponseCode);

            context.Artifact("raw log record", entry);
        });

    private static Scenario Correlation() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.correlation",
            PackageId = Package,
            Title = "Correlation id and error message leakage",
            Summary = "The incoming X-Correlation-ID header is reflected both in the response "
                      + "body's traceId and in the log record's CorrelationId field — the log's "
                      + "TraceId is the request's own identity though, the two differ. Also shows "
                      + "the effect of toggling IncludeExceptionMessage.",
            Fields =
            [
                new ScenarioField("correlationId", "X-Correlation-ID", ScenarioFieldKind.Text, "pinq-12345"),
                new ScenarioField("headerName", "CorrelationIdHeader", ScenarioFieldKind.Text, "X-Correlation-ID"),
            ],
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var correlationId = context.Input.Text("correlationId");
            var headerName = context.Input.Text("headerName");

            await using var hidden = await BuildPipelineAsync(context, options =>
            {
                options.CorrelationIdHeader = headerName;
                options.IncludeExceptionMessage = false;
            });

            using var hiddenRequest = new HttpRequestMessage(HttpMethod.Get, "/throw/unhandled");
            hiddenRequest.Headers.Add(headerName, correlationId);
            using var hiddenResponse = await hidden.Client.SendAsync(hiddenRequest, context.CancellationToken);
            var hiddenBody = await ReadErrorAsync(hiddenResponse, context);

            context.Require("Response traceId is the submitted correlation id", hiddenBody?.TraceId == correlationId,
                hiddenBody?.TraceId);
            context.Require(
                "Internal message does not leak when IncludeExceptionMessage is off",
                hiddenBody?.Message?.Contains("Unexpected error", StringComparison.Ordinal) != true,
                hiddenBody?.Message);
            context.Artifact("body (message hidden)", hiddenBody);

            var entry = await context.WaitForLogAsync(
                record => record.Category.Contains("ExceptionHandlingMiddleware", StringComparison.Ordinal)
                          && record.State.TryGetValue("CorrelationId", out var value)
                          && string.Equals(value?.ToString(), correlationId, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));

            context.Require("Log record has the correlation id", entry is not null);
            var loggedTraceId = entry!.State["TraceId"]?.ToString();
            context.Check(
                "The log's TraceId is the request's own identity, different from the correlation id",
                !string.Equals(loggedTraceId, correlationId, StringComparison.Ordinal),
                $"TraceId={loggedTraceId}");

            await using var revealed = await BuildPipelineAsync(context, options =>
            {
                options.CorrelationIdHeader = headerName;
                options.IncludeExceptionMessage = true;
            });

            using var revealedResponse = await revealed.Client.GetAsync(
                new Uri("/throw/unhandled", UriKind.Relative),
                context.CancellationToken);
            var revealedBody = await ReadErrorAsync(revealedResponse, context);

            context.Require(
                "The real message is returned when IncludeExceptionMessage is on",
                revealedBody?.Message?.Contains("Unexpected error", StringComparison.Ordinal) == true,
                revealedBody?.Message);

            context.Artifact("body (message shown)", revealedBody);
            context.Artifact("log fields", entry.State);
        });

    private static async Task<SandboxPipeline> BuildPipelineAsync(
        ScenarioContext context,
        Action<ErrorHandlingOptions> configure)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(LogLevel.Trace);
                        // Host startup chatter would drown out the middleware record this
                        // scenario exists to show.
                        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                        logging.AddProvider(context.CreateLogProvider());
                    });

                    services.AddPinqponqErrorHandling(configure);
                });

                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.Run(httpContext =>
                    {
                        var caseId = httpContext.Request.Path.Value?.TrimStart('/').Replace("throw/", string.Empty, StringComparison.Ordinal)
                                     ?? string.Empty;
                        Throw(caseId);
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync(context.CancellationToken);

        return new SandboxPipeline(host, host.GetTestClient());
    }

    private static async Task<ErrorResponse?> ReadErrorAsync(HttpResponseMessage response, ScenarioContext context)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                context.CancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ErrorCase(
        string Id,
        string ExceptionLabel,
        int ExpectedStatus,
        string ExpectedResponseCode,
        Action Throw);

    private sealed class SandboxPipeline(IHost host, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            host.Dispose();
        }
    }
}

/// <summary>
/// Exists only to demonstrate the mapping rule that routes any exception whose type name
/// contains "NotFound" to HTTP 404.
/// </summary>
public sealed class WidgetNotFoundException(string message) : Exception(message);
