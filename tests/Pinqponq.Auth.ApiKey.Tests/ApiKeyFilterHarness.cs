using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pinqponq.Auth.ApiKey.DependencyInjection;

namespace Pinqponq.Auth.ApiKey.Tests;

internal sealed class ApiKeyFilterHarness
{
    private readonly ServiceCollection _services = new();

    public CapturingLogger<RequireApiKeyAttribute> Logger { get; } = new();

    public bool IsActionExecuted { get; private set; }

    public ApiKeyFilterHarness()
    {
        _services.AddSingleton<ILogger<RequireApiKeyAttribute>>(Logger);
    }

    public ApiKeyFilterHarness WithKey(string name, string headerName, string? apiKey)
    {
        _services.AddPinqponqApiKey(name, options =>
        {
            options.HeaderName = headerName;
            options.ApiKey = apiKey;
        });
        return this;
    }

    public async Task<IActionResult?> Send(string filterName, IDictionary<string, string>? headers = null)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _services.BuildServiceProvider(),
        };
        foreach (var header in headers ?? new Dictionary<string, string>())
        {
            httpContext.Request.Headers[header.Key] = header.Value;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());

        await new RequireApiKeyAttribute(filterName).OnActionExecutionAsync(executingContext, ExecuteAction);
        return executingContext.Result;

        Task<ActionExecutedContext> ExecuteAction()
        {
            IsActionExecuted = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        }
    }
}
