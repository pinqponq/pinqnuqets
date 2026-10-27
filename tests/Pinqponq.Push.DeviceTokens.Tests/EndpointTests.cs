using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Push.DeviceTokens.DependencyInjection;
using Xunit;

namespace Pinqponq.Push.DeviceTokens.Tests;

[Collection(PostgresCollection.Name)]
public sealed class EndpointTests(DeviceTokenDatabaseFixture database)
{
    private async Task<WebApplication> StartApp(Action<DeviceTokenEndpointOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<TestDbContext>(o => o.UseNpgsql(database.ConnectionString));
        builder.Services.AddPinqponqDeviceTokens<TestDbContext>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("test", null);
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPinqponqDeviceTokenEndpoints(configure);
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, object body, string? user, string claim = "x-user")
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        if (user is not null)
        {
            request.Headers.Add(claim, user);
        }

        return request;
    }

    [Fact]
    public async Task Register_and_unregister_work_for_the_signed_in_user()
    {
        var user = $"user-{Guid.NewGuid():N}";
        var token = $"token-{Guid.NewGuid():N}";
        await using var app = await StartApp();
        using var client = app.GetTestClient();

        var registered = await client.SendAsync(Request(HttpMethod.Post, "/device-tokens/", new { token, platform = "ios", deviceIdentifier = "d1" }, user));
        await using var scope = app.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceTokenRegistry>();
        var afterRegister = await registry.GetTokens(user);
        var unregistered = await client.SendAsync(Request(HttpMethod.Delete, "/device-tokens/", new { token }, user));
        var afterUnregister = await registry.GetTokens(user);

        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        Assert.Equal([token], afterRegister);
        Assert.Equal(HttpStatusCode.NoContent, unregistered.StatusCode);
        Assert.Empty(afterUnregister);
    }

    [Fact]
    public async Task Invalid_registration_gets_400()
    {
        await using var app = await StartApp();
        using var client = app.GetTestClient();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/device-tokens/", new { token = "t", platform = "toaster" }, "u"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("toaster", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unregister_without_a_token_gets_400()
    {
        await using var app = await StartApp();
        using var client = app.GetTestClient();

        var response = await client.SendAsync(Request(HttpMethod.Delete, "/device-tokens/", new { token = "" }, "u"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("token is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_without_authentication_gets_401()
    {
        await using var app = await StartApp();
        using var client = app.GetTestClient();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/device-tokens/", new { token = "t", platform = "ios" }, user: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_request_without_the_user_claim_gets_401()
    {
        await using var app = await StartApp(o => o.UserIdClaimType = "sub");
        using var client = app.GetTestClient();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/device-tokens/", new { token = "t", platform = "ios" }, "u"));
        var deleted = await client.SendAsync(Request(HttpMethod.Delete, "/device-tokens/", new { token = "t" }, "u"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleted.StatusCode);
    }

    [Fact]
    public async Task Route_and_claim_type_are_options()
    {
        var user = $"user-{Guid.NewGuid():N}";
        await using var app = await StartApp(o =>
        {
            o.Route = "/push/tokens";
            o.UserIdClaimType = "sub";
        });
        using var client = app.GetTestClient();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/push/tokens/", new { token = $"t-{user}", platform = "web" }, user, claim: "x-sub"));
        await using var scope = app.Services.CreateAsyncScope();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IDeviceTokenRegistry>().GetTokens(user));
    }

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>();
            if (Request.Headers.TryGetValue("x-user", out var user))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, user.ToString()));
            }

            if (Request.Headers.TryGetValue("x-sub", out var sub))
            {
                claims.Add(new Claim("sub", sub.ToString()));
            }

            if (Request.Headers.Count == 0 || (!Request.Headers.ContainsKey("x-user") && !Request.Headers.ContainsKey("x-sub")))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
