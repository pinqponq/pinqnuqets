using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pinqponq.Hosting.Cors.DependencyInjection;

namespace Pinqponq.Hosting.Cors.Tests;

internal static class CorsHost
{
    public const string AllowedOrigin = "https://app.example.com";
    public const string OtherOrigin = "https://other.example.com";

    public static async Task<IHost> Start(
        Action<PinqponqCorsOptions> configure,
        string environment = "Production",
        string? policyName = null)
    {
        var host = new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services => services.AddRouting().AddPinqponqCors(configure));
                web.Configure(app =>
                {
                    app.UseRouting();
                    if (policyName is null)
                    {
                        app.UseCors();
                    }
                    else
                    {
                        app.UseCors(policyName);
                    }

                    app.UseEndpoints(endpoints => endpoints.MapGet("/ping", () => "pong"));
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    public static Task<HttpResponseMessage> Get(IHost host, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
        request.Headers.Add("Origin", origin);
        return host.GetTestClient().SendAsync(request);
    }

    public static Task<HttpResponseMessage> Preflight(IHost host, string origin, string method = "GET", string? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/ping");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        if (headers is not null)
        {
            request.Headers.Add("Access-Control-Request-Headers", headers);
        }

        return host.GetTestClient().SendAsync(request);
    }

    public static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }
}
