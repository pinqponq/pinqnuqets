using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Cors;

internal sealed class PinqponqCorsOptionsValidator(IHostEnvironment hostEnvironment) : IValidateOptions<PinqponqCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, PinqponqCorsOptions options)
    {
        var failures = new List<string>();
        var origins = options.AllowedOrigins;

        if (origins.Length == 0 && !PinqponqCorsOriginRules.UsesDevelopmentFallback(options, hostEnvironment))
        {
            failures.Add($"{nameof(PinqponqCorsOptions.AllowedOrigins)} must contain at least one origin.");
        }

        if (origins.Contains(PinqponqCorsOriginRules.AnyOrigin) && origins.Length > 1)
        {
            failures.Add($"{nameof(PinqponqCorsOptions.AllowedOrigins)} must contain only '{PinqponqCorsOriginRules.AnyOrigin}' when it allows every origin.");
        }

        foreach (var origin in origins.Where(origin => origin != PinqponqCorsOriginRules.AnyOrigin))
        {
            if (!IsOrigin(origin))
            {
                failures.Add($"{nameof(PinqponqCorsOptions.AllowedOrigins)} entry '{origin}' must be an http or https origin without a path, such as 'https://app.example.com'.");
            }
        }

        if (options.AllowCredentials && PinqponqCorsOriginRules.AllowsEveryOrigin(options, hostEnvironment))
        {
            failures.Add($"{nameof(PinqponqCorsOptions.AllowCredentials)} cannot be combined with an origin list that allows every origin.");
        }

        if (options.PreflightMaxAge is { } maxAge && maxAge <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(PinqponqCorsOptions.PreflightMaxAge)} must be greater than zero.");
        }

        if (options.PolicyName is not null && string.IsNullOrWhiteSpace(options.PolicyName))
        {
            failures.Add($"{nameof(PinqponqCorsOptions.PolicyName)} must not be blank.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isHttp = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        return isHttp && uri.UserInfo.Length == 0 && string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);
    }
}
