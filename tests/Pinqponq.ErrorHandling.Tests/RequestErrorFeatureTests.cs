using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class RequestErrorFeatureTests
{
    [Fact]
    public async Task Handled_exception_is_described_with_its_event_name_and_detail_in_order()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context =>
            {
                context.TraceIdentifier = "trace-1";
                context.Request.Method = HttpMethods.Post;
                context.Request.QueryString = new QueryString("?page=2");
                context.Request.Headers["Language"] = "de";
            });

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal("exception_InvalidOperationException", errorFeature.EventName);
        Assert.Equal(
            ["traceId", "method", "statusCode", "errorCode", "errorMessage", "language", "queryString"],
            errorFeature.Detail.Keys.ToArray());
        Assert.Equal("trace-1", errorFeature.Detail["traceId"]);
        Assert.Equal("POST", errorFeature.Detail["method"]);
        Assert.Equal(400, errorFeature.Detail["statusCode"]);
        Assert.Equal(CommonErrorCodes.ApiBodyRequired, errorFeature.Detail["errorCode"]);
        Assert.Equal("Der Anfragetext ist erforderlich.", errorFeature.Detail["errorMessage"]);
        Assert.Equal("DE", errorFeature.Detail["language"]);
        Assert.Equal("?page=2", errorFeature.Detail["queryString"]);
    }

    [Fact]
    public async Task Detail_has_no_language_or_query_string_when_they_do_not_apply()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException());

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(["traceId", "method", "statusCode", "errorCode", "errorMessage"], errorFeature.Detail.Keys.ToArray());
        Assert.Equal("not_found", errorFeature.Detail["errorCode"]);
        Assert.Equal("The requested resource was not found.", errorFeature.Detail["errorMessage"]);
    }

    [Fact]
    public async Task Started_response_is_left_alone_and_not_described()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException("late failure"),
            context => context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature()));

        Assert.Null(httpContext.Features.Get<IRequestErrorFeature>());
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        var failureLog = Assert.Single(harness.Logger.Records);
        Assert.Equal(LogLevel.Warning, failureLog.Level);
    }

    [Fact]
    public async Task Server_error_is_logged_as_error_and_client_error_as_warning()
    {
        var harness = new ErrorResponseHarness();

        await harness.InvokeAsync(_ => throw new Exception("boom"));
        await harness.InvokeAsync(_ => throw new ArgumentException("bad"));

        Assert.Equal([LogLevel.Error, LogLevel.Warning], harness.Logger.Records.Select(record => record.Level).ToArray());
    }

    [Fact]
    public void Exception_event_name_is_prefixed_type_name()
    {
        Assert.Equal("exception_KeyNotFoundException", ErrorHandlingEvents.BuildExceptionEventName(new KeyNotFoundException()));
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public StartedResponseFeature()
        {
            StatusCode = StatusCodes.Status200OK;
            Body = new MemoryStream();
        }

        public override bool HasStarted => true;
    }
}
