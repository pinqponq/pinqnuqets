using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling.Tests;

internal sealed class ErrorResponseHarness
{
    public ErrorHandlingOptions Options { get; } = new();

    public ErrorLocalizationOptions LocalizationOptions { get; } = new();

    public List<IExceptionResolver> ExceptionResolvers { get; } = [];

    public CapturingLoggerFactory LoggerFactory { get; } = new();

    public CapturingLogger<ExceptionHandlingMiddleware> Logger { get; } = new();

    public ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ExceptionHandlingMiddleware(
            next,
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(LocalizationOptions),
            ExceptionResolvers,
            LoggerFactory,
            Logger);
    }

    public async Task<HttpContext> InvokeAsync(RequestDelegate next, Action<HttpContext>? prepareRequest = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/orders";
        httpContext.Response.Body = new MemoryStream();
        prepareRequest?.Invoke(httpContext);

        await CreateMiddleware(next).InvokeAsync(httpContext);
        return httpContext;
    }

    public static JsonDocument ReadBody(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        return JsonDocument.Parse(httpContext.Response.Body);
    }

    public static string ReadBodyText(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
