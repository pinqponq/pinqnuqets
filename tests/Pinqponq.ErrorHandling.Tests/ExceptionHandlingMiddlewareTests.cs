using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<(IHost Host, HttpClient Client)> StartAsync(
        Action<ErrorHandlingOptions>? configure = null,
        RequestDelegate? next = null)
    {
        next ??= _ => throw new InvalidOperationException("boom");

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddPinqponqErrorHandling(configure);
                });
                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.Run(next);
                });
            })
            .Build();

        await host.StartAsync();
        return (host, host.GetTestClient());
    }

    [Fact]
    public async Task Maps_InvalidOperationException_to_400()
    {
        var (host, client) = await StartAsync();
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.False(body!.Status);
            Assert.Equal(400, body.StatusCode);
            Assert.Equal("bad_request", body.ResponseCode);
            Assert.Equal("The request was invalid.", body.Message);
            Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
        }
    }

    [Fact]
    public async Task Maps_UnauthorizedAccessException_to_401()
    {
        var (host, client) = await StartAsync(next: _ => throw new UnauthorizedAccessException());
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("unauthorized", body!.ResponseCode);
        }
    }

    [Fact]
    public async Task Maps_KeyNotFoundException_to_404()
    {
        var (host, client) = await StartAsync(next: _ => throw new KeyNotFoundException());
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("not_found", body!.ResponseCode);
        }
    }

    [Fact]
    public async Task Maps_unknown_exception_to_500()
    {
        var (host, client) = await StartAsync(next: _ => throw new Exception("secret"));
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("internal_error", body!.ResponseCode);
            Assert.Equal("An unexpected error occurred.", body.Message);
        }
    }

    [Fact]
    public async Task IncludeExceptionMessage_surfaces_detail()
    {
        var (host, client) = await StartAsync(
            o => o.IncludeExceptionMessage = true,
            _ => throw new Exception("secret-detail"));
        using (host)
        {
            var response = await client.GetAsync("/");
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("secret-detail", body!.Message);
        }
    }

    [Fact]
    public async Task Uses_correlation_header_as_traceId()
    {
        var (host, client) = await StartAsync();
        using (host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Add("X-Correlation-ID", "corr-123");
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("corr-123", body!.TraceId);
        }
    }

    [Fact]
    public async Task StatusMappingResolver_overrides_status_and_response_code()
    {
        var (host, client) = await StartAsync(o =>
            o.StatusMappingResolver = ex => ex is InvalidOperationException
                ? new ExceptionStatusMapping(422, "unprocessable")
                : null);
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal(422, body!.StatusCode);
            Assert.Equal("unprocessable", body.ResponseCode);
            Assert.Equal("The request could not be processed.", body.Message);
        }
    }

    [Fact]
    public async Task Response_uses_camelCase_json()
    {
        var (host, client) = await StartAsync();
        using (host)
        {
            var json = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("statusCode", out _));
            Assert.True(doc.RootElement.TryGetProperty("responseCode", out _));
            Assert.True(doc.RootElement.TryGetProperty("traceId", out _));
        }
    }

    [Fact]
    public async Task Non_abort_cancellation_maps_to_504()
    {
        var (host, client) = await StartAsync(next: _ => throw new OperationCanceledException());
        using (host)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("timeout", body!.ResponseCode);
        }
    }

    [Fact]
    public async Task Client_abort_returns_499_when_possible()
    {
        using var cts = new CancellationTokenSource();
        var (host, client) = await StartAsync(next: async context =>
        {
            cts.Cancel();
            await Task.Delay(50, context.RequestAborted);
        });
        using (host)
        {
            try
            {
                await client.GetAsync("/", cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when client cancels; middleware path still covered in-process below.
            }
        }

        // Direct middleware invoke for deterministic 499 coverage.
        var options = Options.Create(new ErrorHandlingOptions());
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(new CancellationToken(canceled: true)),
            options,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.RequestAborted = new CancellationToken(canceled: true);
        await middleware.InvokeAsync(httpContext);
        Assert.Equal(499, httpContext.Response.StatusCode);
    }

    [Fact]
    public void AddPinqponqErrorHandling_registers_options()
    {
        var services = new ServiceCollection();
        services.AddPinqponqErrorHandling(o => o.IncludeExceptionMessage = true);
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorHandlingOptions>>();
        Assert.True(options.Value.IncludeExceptionMessage);
    }
}
