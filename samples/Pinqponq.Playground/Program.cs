using System.Text.Json.Serialization;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Playground.Diagnostics;
using Pinqponq.Playground.Endpoints;
using Pinqponq.Playground.Infrastructure;
using Pinqponq.Playground.Scenarios;
using Pinqponq.Playground.Scenarios.Support;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlaygroundOptions>(
    builder.Configuration.GetSection(PlaygroundOptions.SectionName));

var playgroundOptions = builder.Configuration
    .GetSection(PlaygroundOptions.SectionName)
    .Get<PlaygroundOptions>() ?? new PlaygroundOptions();

// Created before the host so even the earliest framework log lands in the console.
var logSink = new LogSink(playgroundOptions.LogBufferCapacity);
builder.Services.AddSingleton(logSink);
builder.Logging.AddProvider(new CapturingLoggerProvider(logSink));
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Pinqponq", LogLevel.Trace);
builder.Logging.AddFilter("Playground", LogLevel.Trace);
builder.Logging.AddFilter("Testcontainers", LogLevel.Debug);
builder.Logging.AddFilter("DotNet.Testcontainers", LogLevel.Debug);

builder.Services.AddSingleton<DevStackManager>();
builder.Services.AddSingleton<FakeNetGsmState>();
builder.Services.AddSingleton<ScenarioCatalog>();
builder.Services.AddSingleton<ScenarioRunner>();
builder.Services.AddSingleton<MailHogClient>();
builder.Services.AddHttpClient(MailHogClient.HttpClientName);

// Enums are part of the console's vocabulary (service state, field kind); numbers would
// force the frontend to keep a parallel mapping in sync.
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The console dogfoods the package it ships: unhandled errors in its own API return the
// same standard body every consuming application would get. Options are left at their
// defaults so the /sandbox/errors branch shows canonical behaviour — the full exception is
// still one click away in the log console.
builder.Services.AddPinqponqErrorHandling();

var app = builder.Build();

app.UsePinqponqErrorHandling();
app.UseMiddleware<RunCorrelationMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPlayground();

// Probe Docker once at startup, without blocking the first response: the console must be
// usable immediately, and roughly half the scenarios need no containers at all.
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var stack = app.Services.GetRequiredService<DevStackManager>();

lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    await stack.ProbeDockerAsync(CancellationToken.None);
    app.Logger.LogInformation(
        "Playground ready. Docker {DockerState}. Scenario count: {ScenarioCount}.",
        stack.DockerAvailable ? "available" : "unavailable",
        app.Services.GetRequiredService<ScenarioCatalog>().All.Count);
}));

lifetime.ApplicationStopping.Register(() => stack.DisposeAsync().AsTask().GetAwaiter().GetResult());

await app.RunAsync();

/// <summary>
/// Re-attaches a scenario run id carried on an inbound request.
/// </summary>
/// <remarks>
/// The console calls itself over real HTTP for the SMS fake, and the ambient run id does
/// not flow across that boundary. Reading it back from a header keeps those log entries
/// attributed to the run that caused them.
/// </remarks>
internal sealed class RunCorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var runId = context.Request.Headers[RunCorrelation.HeaderName].ToString();
        if (string.IsNullOrEmpty(runId))
        {
            await next(context);
            return;
        }

        using var scope = ScenarioRunContext.Begin(runId);
        await next(context);
    }
}
