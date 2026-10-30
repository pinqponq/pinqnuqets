using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ResponseCodeExceptionRegistrationTests
{
    [Fact]
    public async Task Registered_resolver_answers_a_thrown_response_code()
    {
        using var host = await StartHost(services => services.AddPinqponqResponseCodeExceptions(
            options => options.AddResponseCodes(typeof(SampleResponseCodes))));

        var response = await host.GetTestClient().GetAsync("/orders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SampleResponseCodes.OrderLocked, await ReadResponseCode(response));
    }

    [Fact]
    public async Task Resolver_registered_earlier_wins()
    {
        using var host = await StartHost(services => services
            .AddPinqponqExceptionResolver<ConflictExceptionResolver>()
            .AddPinqponqResponseCodeExceptions(options => options.AddResponseCodes(typeof(SampleResponseCodes))));

        var response = await host.GetTestClient().GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_conflict", await ReadResponseCode(response));
    }

    [Fact]
    public async Task No_registered_code_stops_the_host()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => StartHost(services => services.AddPinqponqResponseCodeExceptions(_ => { })));

        Assert.Contains("No response code is registered", exception.Message);
    }

    [Fact]
    public async Task No_registered_code_is_allowed_when_messages_are_passed_through()
    {
        using var host = await StartHost(services => services.AddPinqponqResponseCodeExceptions(
            options => options.UnknownMessageBehavior = UnknownMessageBehavior.PassMessageThrough));

        var response = await host.GetTestClient().GetAsync("/orders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Resolver_is_registered_once_when_called_twice()
    {
        var services = new ServiceCollection();
        services.AddPinqponqResponseCodeExceptions(options => options.AddResponseCodes(typeof(SampleResponseCodes)));
        services.AddPinqponqResponseCodeExceptions(options => options.AddResponseCodes(["order_archived"]));
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Single(serviceProvider.GetServices<IExceptionResolver>());
        Assert.Contains("order_archived", serviceProvider.GetRequiredService<IOptions<ResponseCodeExceptionOptions>>().Value.ResponseCodes);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqResponseCodeExceptions(_ => { }));
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqResponseCodeExceptions(null!)).ParamName);
    }

    private static async Task<IHost> StartHost(Action<IServiceCollection> configureServices)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting().AddPinqponqErrorHandling();
                    configureServices(services);
                });
                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet(
                        "/orders",
                        new RequestDelegate(_ => throw new InvalidOperationException(SampleResponseCodes.OrderLocked))));
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<string?> ReadResponseCode(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("responseCode").GetString();
    }

    private sealed class ConflictExceptionResolver : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) =>
            new(StatusCodes.Status409Conflict, "order_conflict");
    }
}
