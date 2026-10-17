using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class CancellationAndRegistrationTests
{
    [Fact]
    public async Task Cancellation_by_the_client_is_answered_499_without_a_body()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new TaskCanceledException(),
            context => context.RequestAborted = new CancellationToken(canceled: true));

        Assert.Equal(499, httpContext.Response.StatusCode);
        Assert.Equal(string.Empty, ErrorResponseHarness.ReadBodyText(httpContext));
        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(ErrorHandlingEvents.RequestCancelledByClient, errorFeature.EventName);
        Assert.Equal(499, errorFeature.Detail["statusCode"]);
        Assert.Equal(CommonErrorCodes.ApiClientClosedRequest, errorFeature.Detail["errorCode"]);
        Assert.Equal("Client closed the request.", errorFeature.Detail["errorMessage"]);
        Assert.Equal(LogLevel.Information, Assert.Single(harness.Logger.Records).Level);
    }

    [Fact]
    public async Task Cancellation_not_requested_by_the_client_is_a_timeout()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(_ => throw new TaskCanceledException());

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, httpContext.Response.StatusCode);
        Assert.Equal("timeout", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("exception_TaskCanceledException", httpContext.Features.Get<IRequestErrorFeature>()?.EventName);
    }

    [Fact]
    public async Task Error_body_always_carries_the_trace_id()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new KeyNotFoundException(),
            context => context.TraceIdentifier = "trace-9");

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("trace-9", body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void Registration_helpers_enable_localization_and_register_each_resolver_once()
    {
        var services = new ServiceCollection();
        services.AddPinqponqErrorLocalization(localizationOptions => localizationOptions.LanguageHeader = "Content-Language");
        services.AddPinqponqExceptionResolver<NullExceptionResolver>();
        services.AddPinqponqExceptionResolver<NullExceptionResolver>();
        using var serviceProvider = services.BuildServiceProvider();

        var localizationOptions = serviceProvider.GetRequiredService<IOptions<ErrorLocalizationOptions>>().Value;
        Assert.True(localizationOptions.Enabled);
        Assert.Equal("Content-Language", localizationOptions.LanguageHeader);
        Assert.Single(serviceProvider.GetServices<IExceptionResolver>());
    }

    private sealed class NullExceptionResolver : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) => null;
    }
}
