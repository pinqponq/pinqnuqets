using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pinqloq;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Observability.DependencyInjection;
using Xunit;

namespace Pinqponq.Observability.Tests;

public sealed class RequestLoggingMiddlewareTests
{
    private readonly CapturingLogService _logService = new();

    private async Task<IHost> StartAsync(
        Action<IEndpointRouteBuilder> mapEndpoints,
        Action<ObservabilityOptions>? configure = null,
        bool useErrorHandling = false)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddPinqponqErrorHandling();
                    services.AddSingleton<ILogService>(_logService);
                    services.Configure<ObservabilityOptions>(observabilityOptions =>
                    {
                        observabilityOptions.HttpCollectionName = "app_http";
                        observabilityOptions.JobsCollectionName = "app_jobs";
                        observabilityOptions.ClientCollectionName = "app_client";
                        configure?.Invoke(observabilityOptions);
                    });
                });
                web.Configure(app =>
                {
                    app.UsePinqponqRequestLogging();
                    if (useErrorHandling)
                    {
                        app.UsePinqponqErrorHandling();
                    }

                    app.UseRouting();
                    app.UseEndpoints(mapEndpoints);
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Successful_request_is_one_information_record_named_after_its_route()
    {
        using var host = await StartAsync(endpoints =>
            endpoints.MapGet("/api/orders/{id}", (int id) => Results.Json(new { id, imageUrl = "https://cdn.example/x.png", accessToken = "secret" })));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/5");
        request.Headers.Add("Identifier", "device-1");
        request.Headers.Add(PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName, "corr-1");

        var response = await host.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_logService.Calls);
        Assert.Equal("Info", call.Level);
        Assert.Equal("GET /api/orders/5", call.Message);
        Assert.Equal("get_api_orders_id", call.EventName);
        Assert.Equal("app_http", call.CollectionName);
        Assert.Equal("device-1", call.Identifier);
        Assert.Equal("corr-1", call.CorrelationId);
        Assert.Equal(["traceId", "RequestMethod", "path", "ResponseCode", "elapsedMs", "OutputJson"], call.Fields.Keys.ToArray());
        Assert.Equal("GET", call.Fields["RequestMethod"]);
        Assert.Equal("/api/orders/5", call.Fields["path"]);
        Assert.Equal(200, call.Fields["ResponseCode"]);
        var outputJson = Assert.IsType<Dictionary<string, object?>>(call.Fields["OutputJson"]);
        Assert.Equal(5m, outputJson["id"]);
        Assert.Equal("****", outputJson["imageUrl"]);
        Assert.Equal("***REDACTED***", outputJson["accessToken"]);
    }

    [Fact]
    public async Task Correlation_id_falls_back_to_the_trace_identifier()
    {
        using var host = await StartAsync(endpoints => endpoints.MapGet("/ping", () => "pong"));

        await host.GetTestClient().GetAsync("/ping");

        var call = Assert.Single(_logService.Calls);
        Assert.Equal(call.Fields["traceId"], call.CorrelationId);
        Assert.Equal("pong", call.Fields["OutputJson"]);
    }

    [Fact]
    public async Task Client_error_is_a_warning_and_server_error_an_error()
    {
        using var host = await StartAsync(endpoints =>
        {
            endpoints.MapGet("/missing", () => Results.NotFound());
            endpoints.MapGet("/broken", () => Results.StatusCode(StatusCodes.Status502BadGateway));
        });
        var client = host.GetTestClient();

        await client.GetAsync("/missing");
        await client.GetAsync("/broken");

        Assert.Equal(["Warn", "Error"], _logService.Calls.Select(call => call.Level).ToArray());
        Assert.Null(_logService.Calls[1].Exception);
    }

    [Fact]
    public async Task Failure_answered_by_error_handling_carries_its_event_and_detail()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapGet("/api/orders", IResult () => throw new KeyNotFoundException()),
            useErrorHandling: true);

        var response = await host.GetTestClient().GetAsync("/api/orders?page=2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var call = Assert.Single(_logService.Calls);
        Assert.Equal("Warn", call.Level);
        Assert.Equal("exception_KeyNotFoundException", call.EventName);
        var errorDetail = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(call.Fields["exception"]);
        Assert.Equal("not_found", errorDetail["errorCode"]);
        Assert.Equal("?page=2", errorDetail["queryString"]);
        Assert.Equal(404, call.Fields["ResponseCode"]);
        Assert.Contains("not_found", Assert.IsType<Dictionary<string, object?>>(call.Fields["OutputJson"])["responseCode"]?.ToString());
    }

    [Fact]
    public async Task Unhandled_exception_is_an_error_record_with_the_exception_and_is_rethrown()
    {
        using var host = await StartAsync(endpoints => endpoints.MapGet("/explode", IResult () => throw new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.GetTestClient().GetAsync("/explode"));

        var call = Assert.Single(_logService.Calls);
        Assert.Equal("Error", call.Level);
        Assert.Equal("get_explode", call.EventName);
        Assert.IsType<InvalidOperationException>(call.Exception);
        Assert.Equal(500, call.Fields["ResponseCode"]);
        Assert.False(call.Fields.ContainsKey("OutputJson"));
    }

    [Fact]
    public async Task Catch_all_route_and_unmatched_paths_are_named_after_the_real_path()
    {
        using var host = await StartAsync(endpoints => endpoints.MapGet("/proxy/{**rest}", () => "proxied"));
        var client = host.GetTestClient();

        await client.GetAsync("/proxy/user-api/v1");
        await client.GetAsync("/");
        await client.DeleteAsync("/Unknown/Path");

        Assert.Equal(["get_proxy_user_api_v1", "get_root", "delete_unknown_path"], _logService.Calls.Select(call => call.EventName ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task Excluded_paths_are_not_logged()
    {
        using var host = await StartAsync(endpoints =>
        {
            endpoints.MapGet("/health", () => "ok");
            endpoints.MapGet("/internal/metrics", () => "ok");
        }, observabilityOptions => observabilityOptions.ExcludedPathSegments = ["/health", "/metrics"]);
        var client = host.GetTestClient();

        await client.GetAsync("/health");
        await client.GetAsync("/internal/metrics");

        Assert.Empty(_logService.Calls);
    }

    [Fact]
    public async Task Request_without_device_uid_is_rejected_when_required()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapGet("/api/orders", () => "ok"),
            observabilityOptions => observabilityOptions.RequireDeviceUid = true);

        var response = await host.GetTestClient().GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"status":false,"statusCode":400,"message":"DeviceUid header is required.","responseCode":"DEVICE_UID_REQUIRED"}""",
            await response.Content.ReadAsStringAsync());
        var call = Assert.Single(_logService.Calls);
        Assert.Equal("Warn", call.Level);
        Assert.Equal(ObservabilityEvents.RequestRejectedDeviceUidMissing, call.EventName);
        Assert.Null(call.CollectionName);
        Assert.Equal("DEVICE_UID_REQUIRED", call.Fields["responseCode"]);
        Assert.Equal("/api/orders", call.Fields["path"]);
    }

    [Fact]
    public async Task Device_uid_from_a_header_or_query_or_an_exempt_path_passes_the_check()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapGet("/{**rest}", () => "ok"),
            observabilityOptions =>
            {
                observabilityOptions.RequireDeviceUid = true;
                observabilityOptions.DeviceUidExemptPathSegments = ["/webhooks/"];
            });
        var client = host.GetTestClient();
        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        headerRequest.Headers.Add("X-Device-Uid", "uid-1");

        var headerResponse = await client.SendAsync(headerRequest);
        var queryResponse = await client.GetAsync("/api/orders?device_uid=uid-1");
        var exemptResponse = await client.GetAsync("/webhooks/billing");

        Assert.Equal(HttpStatusCode.OK, headerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exemptResponse.StatusCode);
        Assert.All(_logService.Calls, call => Assert.Equal("Info", call.Level));
    }

    [Fact]
    public async Task Json_request_body_is_logged_with_sensitive_keys_and_urls_redacted()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/api/users", () => Results.NoContent()));
        var body = """{"name":"Ada","password":"p","clientSecret":"s","avatar":"http://cdn/a.png","nested":{"refreshToken":"r"},"items":[1,2.5,true,null,"x"]}""";

        await host.GetTestClient().PostAsync("/api/users", new StringContent(body, Encoding.UTF8, "application/json"));

        var call = Assert.Single(_logService.Calls);
        var inputJson = Assert.IsType<Dictionary<string, object?>>(call.Fields["InputJson"]);
        Assert.Equal("Ada", inputJson["name"]);
        Assert.Equal("***REDACTED***", inputJson["password"]);
        Assert.Equal("***REDACTED***", inputJson["clientSecret"]);
        Assert.Equal("****", inputJson["avatar"]);
        Assert.Equal("***REDACTED***", Assert.IsType<Dictionary<string, object?>>(inputJson["nested"])["refreshToken"]);
        Assert.Equal([1m, 2.5m, true, null, "x"], Assert.IsType<List<object?>>(inputJson["items"]));
        Assert.False(call.Fields.ContainsKey("OutputJson"));
    }

    [Fact]
    public async Task Form_body_and_query_are_logged_with_redaction()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/api/login", () => "ok"));
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "a@b.c", ["password"] = "p", ["returnUrl"] = "https://app" });

        await host.GetTestClient().PostAsync("/api/login?apiKey=k&page=2&next=https://evil", form);

        var call = Assert.Single(_logService.Calls);
        var inputForm = Assert.IsType<Dictionary<string, object?>>(call.Fields["InputJson"]);
        Assert.Equal("a@b.c", inputForm["email"]);
        Assert.Equal("***REDACTED***", inputForm["password"]);
        Assert.Equal("****", inputForm["returnUrl"]);
        var query = Assert.IsType<Dictionary<string, object?>>(call.Fields["query"]);
        Assert.Equal("***REDACTED***", query["apiKey"]);
        Assert.Equal("2", query["page"]);
        Assert.Equal("****", query["next"]);
    }

    [Fact]
    public async Task Multipart_and_binary_bodies_are_omitted()
    {
        using var host = await StartAsync(endpoints => endpoints.MapPost("/upload", () => "ok"));
        var client = host.GetTestClient();
        using var multipart = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.bin" } };
        using var binary = new ByteArrayContent([1, 2, 3]);
        binary.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        await client.PostAsync("/upload", multipart);
        await client.PostAsync("/upload", binary);

        Assert.Equal("[multipart/form-data body omitted]", _logService.Calls[0].Fields["InputJson"]);
        Assert.Equal("[non-text request body omitted]", _logService.Calls[1].Fields["InputJson"]);
    }

    [Fact]
    public async Task Bodies_over_the_limit_are_omitted_or_truncated()
    {
        using var host = await StartAsync(
            endpoints => endpoints.MapPost("/echo", () => Results.Text("response-body-longer-than-limit")),
            observabilityOptions => observabilityOptions.MaxLoggedBodyBytes = 10);
        var client = host.GetTestClient();
        using var withLength = new StringContent("0123456789ABC", Encoding.UTF8, "text/plain");
        using var withoutLength = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("0123456789ABC")));
        withoutLength.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        await client.PostAsync("/echo", withLength);
        await client.PostAsync("/echo", withoutLength);

        Assert.Equal("[request body omitted: body exceeded 10 bytes]", _logService.Calls[0].Fields["InputJson"]);
        Assert.Equal("0123456789\n[truncated: body exceeded 10 bytes]", _logService.Calls[1].Fields["InputJson"]);
        Assert.Equal("response-\n[truncated: body exceeded 10 bytes]".Replace("response-", "response-b"), _logService.Calls[0].Fields["OutputJson"]);
    }

    [Fact]
    public async Task Open_api_documents_are_omitted()
    {
        using var host = await StartAsync(endpoints => endpoints.MapGet("/docs/v1.json", () => Results.Json(new { openapi = "3.0.1", paths = new { } })));

        await host.GetTestClient().GetAsync("/docs/v1.json");

        Assert.Equal("[openapi document omitted]", Assert.Single(_logService.Calls).Fields["OutputJson"]);
    }

    [Fact]
    public async Task Project_items_set_by_the_endpoint_are_added()
    {
        using var host = await StartAsync(endpoints => endpoints.MapGet("/api/keys", (HttpContext httpContext) =>
        {
            httpContext.Items["ProjectId"] = "project-1";
            httpContext.Items["ProjectKey"] = "key-1";
            httpContext.Items["KeyId"] = 7;
            return Results.NoContent();
        }));

        await host.GetTestClient().GetAsync("/api/keys");

        var call = Assert.Single(_logService.Calls);
        Assert.Equal("project-1", call.Fields["projectId"]);
        Assert.Equal("key-1", call.Fields["projectKey"]);
        Assert.Equal("7", call.Fields["keyId"]);
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
