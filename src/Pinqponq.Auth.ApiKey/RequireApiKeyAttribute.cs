using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.ApiKey;

/// <summary>
/// Lets a request through only when its header carries the key registered under
/// <see cref="Name"/> with <c>AddPinqponqApiKey</c>; otherwise the action returns 401.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute(string name) : Attribute, IAsyncActionFilter
{
    /// <summary>The name the key settings were registered under.</summary>
    public string Name { get; } = name;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var requestServices = context.HttpContext.RequestServices;
        var apiKeyOptions = requestServices.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get(Name);

        if (string.IsNullOrEmpty(apiKeyOptions.ApiKey))
        {
            requestServices.GetRequiredService<ILogger<RequireApiKeyAttribute>>().LogWarning(
                "API key '{ApiKeyName}' has no configured key; the request was refused.", Name);
            context.Result = new UnauthorizedResult();
            return;
        }

        var providedKey = context.HttpContext.Request.Headers[apiKeyOptions.HeaderName].ToString();
        if (!ApiKeyComparison.IsMatch(providedKey, apiKeyOptions.ApiKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}
