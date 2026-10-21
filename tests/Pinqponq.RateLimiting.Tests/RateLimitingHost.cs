using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinqponq.RateLimiting.DependencyInjection;

namespace Pinqponq.RateLimiting.Tests;

/// <summary>
/// A test server with Pinqponq rate limiting. The test client cannot set the client address or the
/// user, so the requests carry them in test headers that a middleware before UseRateLimiter applies.
/// </summary>
internal sealed class RateLimitingHost : IAsyncDisposable
{
    public const string PolicyName = "test-policy";
    public const string ClientAddressHeader = "X-Test-Client-Address";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string PartitionValueHeader = "X-Test-Partition";
    public const string PolicyPath = "/policy";
    public const string UnlimitedPath = "/unlimited";

    private readonly IHost _host;

    private RateLimitingHost(IHost host, CapturingLoggerProvider loggerProvider)
    {
        _host = host;
        LoggerProvider = loggerProvider;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider LoggerProvider { get; }

    public static async Task<RateLimitingHost> Start(
        Action<IServiceCollection> configureServices,
        string userIdClaimType = ClaimTypes.NameIdentifier)
    {
        var loggerProvider = new CapturingLoggerProvider();
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddProvider(loggerProvider));
                    services.AddRouting();
                    configureServices(services);
                });
                web.Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        ApplyTestIdentity(context, userIdClaimType);
                        return next(context);
                    });
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(PolicyPath, () => "ok").RequireRateLimiting(PolicyName);
                        endpoints.MapGet(UnlimitedPath, () => "ok");
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new RateLimitingHost(host, loggerProvider);
    }

    public static Task<RateLimitingHost> StartWithPolicy(RateLimitPolicyOptions policy) =>
        Start(services => services.AddPinqponqRateLimiting(options => options.Policies.Add(PolicyName, policy)));

    public Task<HttpResponseMessage> Send(
        string path,
        string? clientAddress = null,
        string? userId = null,
        string? partitionValue = null,
        string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeader(request, ClientAddressHeader, clientAddress);
        AddHeader(request, UserIdHeader, userId);
        AddHeader(request, PartitionValueHeader, partitionValue);
        AddHeader(request, "Accept-Language", language);
        return Client.SendAsync(request);
    }

    public async Task<List<HttpStatusCode>> SendMany(int requestCount, Func<Task<HttpResponseMessage>> send)
    {
        var statusCodes = new List<HttpStatusCode>();
        for (var requestIndex = 0; requestIndex < requestCount; requestIndex++)
        {
            using var response = await send();
            statusCodes.Add(response.StatusCode);
        }

        return statusCodes;
    }

    public static string? ReadPartitionValue(HttpContext context) =>
        context.Request.Headers[PartitionValueHeader].FirstOrDefault();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static void ApplyTestIdentity(HttpContext context, string userIdClaimType)
    {
        var clientAddress = context.Request.Headers[ClientAddressHeader].FirstOrDefault();
        context.Connection.RemoteIpAddress = clientAddress is null ? null : IPAddress.Parse(clientAddress);

        var userId = context.Request.Headers[UserIdHeader].FirstOrDefault();
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(userIdClaimType, userId)], "Test"));
        }
    }

    private static void AddHeader(HttpRequestMessage request, string headerName, string? headerValue)
    {
        if (headerValue is not null)
        {
            request.Headers.Add(headerName, headerValue);
        }
    }
}
