using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.OpenApi.DependencyInjection;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

/// <summary>
/// A test server with Pinqponq OpenAPI and four endpoints: one needs authorization, one allows anonymous
/// callers inside an authorized group, one has no authorization metadata, and one is a model-returning endpoint.
/// </summary>
internal sealed class OpenApiHost : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";
    public const string SecurePath = "/secure";
    public const string OpenPath = "/open";
    public const string AllowAnonymousPath = "/guarded/anonymous";
    public const string GuardedPath = "/guarded/private";
    public const string ModelPath = "/model";
    public const string DefaultDocumentPath = "/openapi/v1.json";
    public const string DefaultScalarPath = "/scalar/v1";

    private readonly IHost _host;

    private OpenApiHost(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public static async Task<OpenApiHost> Start(
        Action<PinqponqOpenApiOptions>? configure = null,
        string environmentName = "Development",
        bool registerBearerAuthentication = true,
        Action<IServiceCollection>? configureServices = null,
        string? pathBase = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(environmentName);
                web.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
                    services.AddRouting();
                    AddAuthentication(services, registerBearerAuthentication);
                    services.AddAuthorizationBuilder()
                        .AddPolicy("docs", policy => policy.RequireClaim("docs"));
                    services.AddPinqponqOpenApi(configure);
                    configureServices?.Invoke(services);
                });
                web.Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Loopback;
                        return next(context);
                    });
                    app.UseForwardedHeaders(new ForwardedHeadersOptions
                    {
                        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
                    });
                    if (pathBase is not null)
                    {
                        app.UsePathBase(pathBase);
                    }

                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPinqponqOpenApi();
                        MapEndpoints(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new OpenApiHost(host);
    }

    public async Task<JsonDocument> GetDocument(string path = DefaultDocumentPath)
    {
        using var response = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static void AddAuthentication(IServiceCollection services, bool registerBearerAuthentication)
    {
        var schemeName = registerBearerAuthentication ? "Bearer" : "Other";
        services.AddAuthentication(schemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(schemeName, null);
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SecurePath, () => "secure").RequireAuthorization();
        endpoints.MapGet(OpenPath, () => "open");

        var guarded = endpoints.MapGroup("/guarded").RequireAuthorization();
        guarded.MapGet("/private", () => "private");
        guarded.MapGet("/anonymous", () => "anonymous").AllowAnonymous();

        endpoints.MapGet(ModelPath, () => new Nested.OrderModel("id"));
    }

    public sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, urlEncoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("docs", "true")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

internal static class Nested
{
    internal sealed record OrderModel(string Id);
}
