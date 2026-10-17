using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ExceptionResolverTests
{
    [Fact]
    public async Task Resolver_runs_before_the_status_mapping_resolver()
    {
        var harness = new ErrorResponseHarness();
        harness.Options.StatusMappingResolver = _ => new ExceptionStatusMapping(StatusCodes.Status418ImATeapot, "teapot");
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(new ExceptionResolution(StatusCodes.Status409Conflict, "order_conflict")));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("conflict"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("order_conflict", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("The request conflicts with the current state.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Resolver_returning_null_falls_through_to_the_status_mapping_resolver()
    {
        var harness = new ErrorResponseHarness();
        harness.Options.StatusMappingResolver = _ => new ExceptionStatusMapping(StatusCodes.Status418ImATeapot, "teapot");
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(resolution: null));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("conflict"));

        Assert.Equal(StatusCodes.Status418ImATeapot, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Throwing_resolver_is_logged_and_the_next_one_answers()
    {
        var harness = new ErrorResponseHarness();
        harness.ExceptionResolvers.Add(new ThrowingExceptionResolver());
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(new ExceptionResolution(StatusCodes.Status409Conflict, "order_conflict")));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("conflict"));

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        var resolverError = Assert.Single(harness.Logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains(nameof(ThrowingExceptionResolver), resolverError.Message);
    }

    [Fact]
    public async Task Localized_resolution_uses_its_own_code_instead_of_the_exception_message()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.ExceptionMessageIsResponseCode = true;
        harness.LocalizationOptions.CatalogDirectory = null;
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(new ExceptionResolution(StatusCodes.Status400BadRequest, CommonErrorCodes.ApiPageNumberInvalid)));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(CommonErrorCodes.ApiPageNumberInvalid, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Page number must be greater than 0.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Localized_resolution_with_an_unknown_code_uses_its_fallback_code()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.ExceptionMessageIsResponseCode = true;
        harness.LocalizationOptions.CatalogDirectory = null;
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(new ExceptionResolution(StatusCodes.Status400BadRequest, "unknown_code")
        {
            FallbackResponseCode = CommonErrorCodes.ApiBodyRequired,
        }));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("driver failure"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(CommonErrorCodes.ApiBodyRequired, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Resolution_names_the_event_after_the_reported_exception_and_appends_its_detail()
    {
        var harness = new ErrorResponseHarness();
        var reportedException = new TimeoutException("driver timeout");
        harness.ExceptionResolvers.Add(new FixedExceptionResolver(new ExceptionResolution(StatusCodes.Status400BadRequest, "driver_error")
        {
            ReportedException = reportedException,
            LogDetail = new Dictionary<string, object?> { ["sqlState"] = "57014", ["tableName"] = null },
        }));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("wrapper", reportedException));

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal("exception_TimeoutException", errorFeature.EventName);
        Assert.Equal(
            ["traceId", "method", "statusCode", "errorCode", "errorMessage", "sqlState", "tableName"],
            errorFeature.Detail.Keys.ToArray());
        Assert.Equal("57014", errorFeature.Detail["sqlState"]);
        Assert.Null(errorFeature.Detail["tableName"]);
    }

    private sealed class FixedExceptionResolver(ExceptionResolution? resolution) : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) => resolution;
    }

    private sealed class ThrowingExceptionResolver : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) => throw new InvalidOperationException("resolver bug");
    }
}
