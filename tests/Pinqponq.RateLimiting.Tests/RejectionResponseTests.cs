using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.RateLimiting.DependencyInjection;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class RejectionResponseTests
{
    private static RateLimitPolicyOptions OneRequestPerMinute() => new()
    {
        PermitLimit = 1,
        Window = TimeSpan.FromMinutes(1),
    };

    [Fact]
    public async Task Request_over_the_limit_gets_429_with_retry_after_and_the_error_body()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute());

        using var allowedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1");
        using var rejectedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1");

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        var retryAfterSeconds = int.Parse(Assert.Single(rejectedResponse.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfterSeconds, 1, 60);

        using var body = JsonDocument.Parse(await rejectedResponse.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(StatusCodes.Status429TooManyRequests, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Too many requests. Try again later.", body.RootElement.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Rejection_message_is_localized_when_error_localization_is_on()
    {
        await using var host = await RateLimitingHost.Start(services =>
        {
            services.AddPinqponqErrorLocalization(options => options.CatalogDirectory = null);
            services.AddPinqponqRateLimiting(options => options.Policies.Add(RateLimitingHost.PolicyName, OneRequestPerMinute()));
        });

        using var allowedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1");
        using var rejectedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", language: "tr-TR");

        using var body = JsonDocument.Parse(await rejectedResponse.Content.ReadAsStringAsync());
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Çok fazla istek gönderildi. Daha sonra tekrar deneyin.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Rejection_writer_registered_before_the_package_writes_the_body()
    {
        await using var host = await RateLimitingHost.Start(services =>
        {
            services.AddSingleton<IRateLimitRejectionWriter, PlainTextRejectionWriter>();
            services.AddPinqponqRateLimiting(options => options.Policies.Add(RateLimitingHost.PolicyName, OneRequestPerMinute()));
        });

        await AssertPlainTextRejection(host);
    }

    [Fact]
    public async Task Rejection_writer_registered_after_the_package_writes_the_body()
    {
        await using var host = await RateLimitingHost.Start(services =>
        {
            services.AddPinqponqRateLimiting(options => options.Policies.Add(RateLimitingHost.PolicyName, OneRequestPerMinute()));
            services.AddSingleton<IRateLimitRejectionWriter, PlainTextRejectionWriter>();
        });

        await AssertPlainTextRejection(host);
    }

    private static async Task AssertPlainTextRejection(RateLimitingHost host)
    {
        using var allowedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1");
        using var rejectedResponse = await host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        var retryAfter = Assert.Single(rejectedResponse.Headers.GetValues("Retry-After"));
        Assert.Equal($"wait {retryAfter}", await rejectedResponse.Content.ReadAsStringAsync());
    }

    private sealed class PlainTextRejectionWriter : IRateLimitRejectionWriter
    {
        public Task Write(HttpContext context, int? retryAfterSeconds, CancellationToken cancellationToken) =>
            context.Response.WriteAsync($"wait {retryAfterSeconds}", cancellationToken);
    }
}
