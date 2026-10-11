using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Observability.DependencyInjection;
using Xunit;

namespace Pinqponq.Observability.Tests;

public sealed class ObservabilityEdgeCaseTests
{
    private readonly CapturingLogService _logService = new();

    private async Task<IHost> StartAsync(Action<IEndpointRouteBuilder> mapEndpoints, Action<ObservabilityOptions>? configure = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ILogService>(_logService);
                    services.Configure<ObservabilityOptions>(observabilityOptions =>
                    {
                        observabilityOptions.HttpCollectionName = "app_http";
                        configure?.Invoke(observabilityOptions);
                    });
                });
                web.Configure(app =>
                {
                    app.UsePinqponqRequestLogging();
                    app.UseRouting();
                    app.UseEndpoints(mapEndpoints);
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static RequestLoggingMiddleware CreateMiddleware(RequestDelegate next, int maxLoggedBodyBytes = 1024) =>
        new(next, Options.Create(new ObservabilityOptions { HttpCollectionName = "app_http", MaxLoggedBodyBytes = maxLoggedBodyBytes }));

    [Fact]
    public async Task Response_body_reaches_the_client_on_logged_and_excluded_paths()
    {
        using var host = await StartAsync(endpoints =>
        {
            endpoints.MapGet("/api/orders", () => "orders");
            endpoints.MapGet("/health", () => "healthy");
        });
        var client = host.GetTestClient();

        Assert.Equal("orders", await client.GetStringAsync("/api/orders"));
        Assert.Equal("healthy", await client.GetStringAsync("/health"));
        Assert.Single(_logService.Calls);
    }

    [Fact]
    public async Task Status_500_is_an_error_and_status_400_a_warning()
    {
        using var host = await StartAsync(endpoints =>
        {
            endpoints.MapGet("/server", () => Results.StatusCode(StatusCodes.Status500InternalServerError));
            endpoints.MapGet("/client", () => Results.StatusCode(StatusCodes.Status400BadRequest));
            endpoints.MapGet("/redirect", () => Results.StatusCode(StatusCodes.Status302Found));
        });
        var client = host.GetTestClient();

        await client.GetAsync("/server");
        await client.GetAsync("/client");
        await client.GetAsync("/redirect");

        Assert.Equal(["Error", "Warn", "Info"], _logService.Calls.Select(call => call.Level).ToArray());
    }

    [Fact]
    public async Task Endpoint_can_read_the_request_body_after_it_was_logged()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/echo", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            return Results.Text(await reader.ReadToEndAsync());
        }));

        var response = await host.GetTestClient().PostAsync("/echo", new StringContent("""{"name":"Ada"}""", Encoding.UTF8, "application/json"));

        Assert.Equal("""{"name":"Ada"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("Ada", Assert.IsType<Dictionary<string, object?>>(Assert.Single(_logService.Calls).Fields["InputJson"])["name"]);
    }

    [Fact]
    public async Task Bodies_of_exactly_the_limit_are_logged_whole()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapPost("/echo", () => Results.Text("0123456789")),
            observabilityOptions => observabilityOptions.MaxLoggedBodyBytes = 10);
        var client = host.GetTestClient();
        using var withLength = new StringContent("ABCDEFGHIJ", Encoding.UTF8, "text/plain");
        using var withoutLength = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("ABCDEFGHIJ")));
        withoutLength.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        await client.PostAsync("/echo", withLength);
        await client.PostAsync("/echo", withoutLength);

        Assert.All(_logService.Calls, call =>
        {
            Assert.Equal("ABCDEFGHIJ", call.Fields["InputJson"]);
            Assert.Equal("0123456789", call.Fields["OutputJson"]);
        });
    }

    [Fact]
    public async Task Json_arrays_and_objects_without_paths_are_logged_as_json()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/json", () => Results.Text("""{"openapi":"3.0.1"}""", "application/json")));

        await host.GetTestClient().PostAsync("/json", new StringContent("  [1,\"a\"]", Encoding.UTF8, "application/json"));

        var call = Assert.Single(_logService.Calls);
        Assert.Equal([1m, "a"], Assert.IsType<List<object?>>(call.Fields["InputJson"]));
        Assert.Equal("3.0.1", Assert.IsType<Dictionary<string, object?>>(call.Fields["OutputJson"])["openapi"]);
    }

    [Fact]
    public async Task Text_that_only_looks_like_json_is_logged_as_text()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/text", () => Results.NoContent()));

        await host.GetTestClient().PostAsync("/text", new StringContent("{ not json", Encoding.UTF8, "text/plain"));

        Assert.Equal("{ not json", Assert.Single(_logService.Calls).Fields["InputJson"]);
    }

    [Fact]
    public async Task Every_sensitive_key_is_redacted()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/keys", () => Results.NoContent()));
        var body = """{"Authorization":"a","cookie":"b","Set-Cookie":"c","X-Internal-Key":"d","apikey":"e","X-Api-Key":"f","mySecret":"g","idToken":"h","userPassword":"i","plain":"j"}""";

        await host.GetTestClient().PostAsync("/keys", new StringContent(body, Encoding.UTF8, "application/json"));

        var inputJson = Assert.IsType<Dictionary<string, object?>>(Assert.Single(_logService.Calls).Fields["InputJson"]);
        Assert.All(inputJson.Where(field => field.Key != "plain"), field => Assert.Equal("***REDACTED***", field.Value));
        Assert.Equal("j", inputJson["plain"]);
    }

    [Fact]
    public async Task Device_uid_rejection_is_a_json_response_with_its_own_record()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapGet("/api/feed", () => "feed"),
            observabilityOptions => observabilityOptions.RequireDeviceUid = true);
        var client = host.GetTestClient();
        using var dashedHeaderRequest = new HttpRequestMessage(HttpMethod.Get, "/api/feed");
        dashedHeaderRequest.Headers.Add("Device-Uid", "uid-1");

        var rejected = await client.GetAsync("/api/feed");
        var accepted = await client.SendAsync(dashedHeaderRequest);

        Assert.Equal("application/json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var rejection = _logService.Calls[0];
        Assert.Equal("Request rejected because DeviceUid header is missing.", rejection.Message);
        Assert.Equal(["traceId", "method", "path", "responseCode"], rejection.Fields.Keys.ToArray());
        Assert.Equal("GET", rejection.Fields["method"]);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Fields["traceId"]?.ToString()));
    }

    [Fact]
    public async Task Partial_response_written_before_a_failure_still_reaches_the_client()
    {
        var middleware = CreateMiddleware(async context =>
        {
            await context.Response.WriteAsync("partial");
            throw new InvalidOperationException("late failure");
        });
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/stream";
        var clientStream = new MemoryStream();
        httpContext.Response.Body = clientStream;

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(httpContext, _logService));

        Assert.Equal("partial", Encoding.UTF8.GetString(clientStream.ToArray()));
        Assert.Same(clientStream, httpContext.Response.Body);
        var call = Assert.Single(_logService.Calls);
        Assert.Equal("GET /stream", call.Message);
        Assert.Equal("app_http", call.CollectionName);
    }

    [Fact]
    public async Task Failure_before_any_output_writes_nothing_to_the_client()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("early failure"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        var clientStream = new MemoryStream();
        httpContext.Response.Body = clientStream;

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(httpContext, _logService));

        Assert.Equal(0, clientStream.Length);
        Assert.Equal("GET ", Assert.Single(_logService.Calls).Message);
    }

    [Fact]
    public void Registration_helpers_reject_missing_arguments()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqObservability(configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqObservability(null!));
        Assert.Throws<ArgumentNullException>(() => ((IApplicationBuilder)null!).UsePinqponqRequestLogging());
        Assert.Throws<ArgumentNullException>(() => ((IEndpointRouteBuilder)null!).MapPinqponqClientLogs());
    }

    [Fact]
    public void Configure_callback_also_sets_the_pinqloq_jobs_collection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Observability:JobsCollectionName"] = "from_settings", ["Pinqloq:SecretKey"] = "pinqloq-secret" })
            .Build();
        var services = new ServiceCollection();
        services.AddPinqponqObservability(configuration, observabilityOptions => observabilityOptions.JobsCollectionName = "from_code");
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Equal("from_code", serviceProvider.GetRequiredService<IOptions<Pinqloq.PinqloqOptions>>().Value.ApiLogsCollectionName);
    }

    [Fact]
    public void Client_log_level_defaults_to_information()
    {
        Assert.Equal("Information", new ClientLogRequest { Event = "app_started" }.LogLevel);
    }

    [Fact]
    public void Validator_accepts_complete_options_and_lists_every_problem()
    {
        var validator = new ObservabilityOptionsValidator();

        var valid = validator.Validate(null, new ObservabilityOptions { HttpCollectionName = "h", JobsCollectionName = "j", ClientCollectionName = "c" });
        var invalid = validator.Validate(null, new ObservabilityOptions { HttpCollectionName = " ", JobsCollectionName = "j", ClientCollectionName = "c", MaxLoggedBodyBytes = -1 });

        Assert.True(valid.Succeeded);
        Assert.True(invalid.Failed);
        Assert.Equal(
            ["Observability:HttpCollectionName is required.", "Observability:MaxLoggedBodyBytes must be greater than 0."],
            invalid.Failures!.ToArray());
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
