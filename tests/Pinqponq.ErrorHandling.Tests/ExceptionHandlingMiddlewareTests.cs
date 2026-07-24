using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body!.Status.Should().BeFalse();
            body.StatusCode.Should().Be(400);
            body.ResponseCode.Should().Be("bad_request");
            body.Message.Should().Be("The request was invalid.");
            body.TraceId.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Maps_UnauthorizedAccessException_to_401()
    {
        var (host, client) = await StartAsync(next: _ => throw new UnauthorizedAccessException());
        using (host)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body!.ResponseCode.Should().Be("unauthorized");
        }
    }

    [Fact]
    public async Task Maps_KeyNotFoundException_to_404()
    {
        var (host, client) = await StartAsync(next: _ => throw new KeyNotFoundException());
        using (host)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body!.ResponseCode.Should().Be("not_found");
        }
    }

    [Fact]
    public async Task Maps_unknown_exception_to_500()
    {
        var (host, client) = await StartAsync(next: _ => throw new Exception("secret"));
        using (host)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body!.ResponseCode.Should().Be("internal_error");
            body.Message.Should().Be("An unexpected error occurred.");
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
            body!.Message.Should().Be("secret-detail");
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
            body!.TraceId.Should().Be("corr-123");
        }
    }

    [Fact]
    public async Task StatusCodeResolver_overrides_status()
    {
        var (host, client) = await StartAsync(o =>
            o.StatusCodeResolver = ex => ex is InvalidOperationException ? 422 : null);
        using (host)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be((HttpStatusCode)422);
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body!.StatusCode.Should().Be(422);
            body.ResponseCode.Should().Be("bad_request");
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
            doc.RootElement.TryGetProperty("statusCode", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("responseCode", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
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
        httpContext.Response.StatusCode.Should().Be(499);
    }

    [Fact]
    public void AddPinqponqErrorHandling_registers_options()
    {
        var services = new ServiceCollection();
        services.AddPinqponqErrorHandling(o => o.IncludeExceptionMessage = true);
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorHandlingOptions>>()
            .Value.IncludeExceptionMessage.Should().BeTrue();
    }
}
