using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Playground.Diagnostics;
using Pinqponq.Playground.Infrastructure;
using Pinqponq.Playground.Scenarios;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Endpoints;

/// <summary>Maps the console's HTTP surface.</summary>
public static class PlaygroundEndpoints
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Catalog, runs, infrastructure control, logs, mail and the in-process fakes.</summary>
    public static void MapPlayground(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCatalog(app);
        MapScenarios(app);
        MapInfrastructure(app);
        MapLogs(app);
        MapMail(app);
        MapFakeNetGsm(app);
        MapErrorSandbox(app);
    }

    private static void MapCatalog(WebApplication app)
    {
        app.MapGet("/api/catalog", (ScenarioCatalog catalog, DevStackManager stack) => Results.Ok(new
        {
            dockerAvailable = stack.DockerAvailable,
            dockerError = stack.DockerError,
            packages = ScenarioCatalog.Packages.Select(package => new
            {
                package.Id,
                package.Title,
                package.Group,
                package.Summary,
                scenarios = catalog.All
                    .Where(scenario => string.Equals(scenario.Descriptor.PackageId, package.Id, StringComparison.Ordinal))
                    .Select(scenario => Describe(scenario.Descriptor, stack)),
            }),
        }));

        app.MapGet("/api/sandbox/cases", () => Results.Ok(ErrorHandlingScenarios.Expectations()));
    }

    private static void MapScenarios(WebApplication app)
    {
        app.MapPost("/api/scenarios/{id}/run", async (
            string id,
            [FromBody] ScenarioRunRequest? request,
            ScenarioCatalog catalog,
            ScenarioRunner runner,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!catalog.Contains(id))
            {
                return Results.NotFound(new { message = $"Bilinmeyen senaryo: {id}" });
            }

            var baseAddress = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
            var result = await runner.RunAsync(
                id,
                request?.Input ?? new Dictionary<string, string?>(StringComparer.Ordinal),
                baseAddress,
                cancellationToken);

            return Results.Ok(result);
        });
    }

    private static void MapInfrastructure(WebApplication app)
    {
        app.MapGet("/api/infra", (DevStackManager stack) => Results.Ok(new
        {
            dockerAvailable = stack.DockerAvailable,
            dockerError = stack.DockerError,
            services = stack.GetAll(),
        }));

        app.MapPost("/api/infra/probe", async (DevStackManager stack, CancellationToken cancellationToken) =>
        {
            await stack.ProbeDockerAsync(cancellationToken);
            return Results.Ok(new { dockerAvailable = stack.DockerAvailable, dockerError = stack.DockerError });
        });

        // Starting a container can take minutes on a cold image cache, so the call returns
        // immediately and progress arrives over the status stream.
        app.MapPost("/api/infra/{id}/start", (string id, DevStackManager stack) =>
        {
            _ = Task.Run(() => stack.StartAsync(id, CancellationToken.None));
            return Results.Accepted($"/api/infra/{id}", stack.Get(id));
        });

        app.MapPost("/api/infra/{id}/stop", async (string id, DevStackManager stack, CancellationToken cancellationToken) =>
            Results.Ok(await stack.StopAsync(id, cancellationToken)));

        app.MapPost("/api/infra/{id}/restart", (string id, DevStackManager stack) =>
        {
            _ = Task.Run(() => stack.RestartAsync(id, CancellationToken.None));
            return Results.Accepted($"/api/infra/{id}", stack.Get(id));
        });

        app.MapGet("/api/infra/stream", async (HttpContext http, DevStackManager stack, CancellationToken cancellationToken) =>
        {
            PrepareEventStream(http);
            using var subscription = stack.Subscribe();

            foreach (var status in stack.GetAll())
            {
                await WriteEventAsync(http, "service", status, cancellationToken);
            }

            await PumpAsync(http, subscription.Reader.ReadAllAsync(cancellationToken), "service", cancellationToken);
        });
    }

    private static void MapLogs(WebApplication app)
    {
        app.MapGet("/api/logs", (
            LogSink sink,
            string? level,
            string? category,
            string? q,
            string? runId,
            long? sinceId,
            int? take) => Results.Ok(new
            {
                count = sink.Count,
                entries = sink.Query(new LogQuery
                {
                    MinLevel = level,
                    Category = category,
                    Search = q,
                    RunId = runId,
                    SinceId = sinceId,
                    Take = Math.Clamp(take ?? 300, 1, 2000),
                }),
            }));

        app.MapDelete("/api/logs", (LogSink sink) =>
        {
            sink.Clear();
            return Results.NoContent();
        });

        app.MapGet("/api/logs/stream", async (HttpContext http, LogSink sink, CancellationToken cancellationToken) =>
        {
            PrepareEventStream(http);
            using var subscription = sink.Subscribe();
            await PumpAsync(http, subscription.Reader.ReadAllAsync(cancellationToken), "log", cancellationToken);
        });
    }

    private static void MapMail(WebApplication app)
    {
        app.MapGet("/api/mail", async (MailHogClient mailhog, int? take, CancellationToken cancellationToken) =>
        {
            if (!mailhog.IsAvailable)
            {
                return Results.Json(
                    new { reason = "mailhog-hazir-degil", message = "MailHog servisi çalışmıyor." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(await mailhog.ListAsync(Math.Clamp(take ?? 50, 1, 200), cancellationToken));
        });

        app.MapDelete("/api/mail", async (MailHogClient mailhog, CancellationToken cancellationToken) =>
        {
            if (!mailhog.IsAvailable)
            {
                return Results.Json(
                    new { reason = "mailhog-hazir-degil" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await mailhog.ClearAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapFakeNetGsm(WebApplication app)
    {
        // Deliberately outside /api: this is the URL SmsOptions.ApiUrl points at, and it
        // should read like a third-party endpoint.
        app.MapGet("/fake-netgsm/sms/send/get", (HttpContext http, FakeNetGsmState state) =>
        {
            var query = http.Request.Query;
            var record = state.Record(
                query["usercode"],
                query["gsmno"],
                query["message"],
                query["msgheader"],
                http.Request.QueryString.Value ?? string.Empty,
                ScenarioRunContext.RunId);

            return record.ResponseStatus == 200
                ? Results.Text("00", "text/plain")
                : Results.Text("sunucu hatası", "text/plain", Encoding.UTF8, StatusCodes.Status500InternalServerError);
        });

        app.MapGet("/api/sms", (FakeNetGsmState state) => Results.Ok(new
        {
            failuresRemaining = state.FailuresRemaining,
            requests = state.Requests,
        }));

        app.MapPost("/api/sms/fail-next", (FakeNetGsmState state, int? count) =>
        {
            state.FailNext(count ?? 1);
            return Results.Ok(new { failuresRemaining = state.FailuresRemaining });
        });

        app.MapDelete("/api/sms", (FakeNetGsmState state) =>
        {
            state.Reset();
            return Results.NoContent();
        });
    }

    private static void MapErrorSandbox(IApplicationBuilder app)
    {
        // A real pipeline branch, so the wire format can be inspected with curl. Scenario
        // runs build their own throwaway pipeline instead, because the middleware captures
        // its options when the pipeline is constructed.
        app.Map("/sandbox/errors", branch =>
        {
            branch.UsePinqponqErrorHandling();
            branch.Run(http =>
            {
                var caseId = http.Request.Path.Value?.Trim('/') ?? string.Empty;
                ErrorHandlingScenarios.Throw(caseId);
                return Task.CompletedTask;
            });
        });
    }

    private static object Describe(ScenarioDescriptor descriptor, DevStackManager stack)
    {
        var blocked = descriptor.RequiredServices.Where(id => !stack.IsReady(id)).ToArray();
        return new
        {
            descriptor.Id,
            descriptor.PackageId,
            descriptor.Title,
            descriptor.Summary,
            descriptor.RequiredServices,
            descriptor.NegativePath,
            descriptor.NeedsInternet,
            descriptor.TimeoutSeconds,
            available = blocked.Length == 0,
            blockedBy = blocked,
            fields = descriptor.Fields,
        };
    }

    private static void PrepareEventStream(HttpContext http)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        // Without this a buffering reverse proxy holds the stream until it closes.
        http.Response.Headers["X-Accel-Buffering"] = "no";
    }

    private static async Task PumpAsync<T>(
        HttpContext http,
        IAsyncEnumerable<T> source,
        string eventName,
        CancellationToken cancellationToken)
    {
        var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        try
        {
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                await WriteEventAsync(http, eventName, item, cancellationToken);

                if (heartbeat.IsCompleted)
                {
                    await http.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await http.Response.Body.FlushAsync(cancellationToken);
                    heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The browser navigated away or closed the EventSource.
        }
    }

    private static async Task WriteEventAsync<T>(
        HttpContext http,
        string eventName,
        T payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, StreamJson);
        await http.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>Body of a scenario run request.</summary>
public sealed record ScenarioRunRequest(Dictionary<string, string?>? Input);
