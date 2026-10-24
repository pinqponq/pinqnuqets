using Microsoft.Extensions.Options;

namespace Pinqponq.OpenApi;

internal sealed class PinqponqOpenApiOptionsValidator : IValidateOptions<PinqponqOpenApiOptions>
{
    private const string DocumentNamePlaceholder = "{documentName}";

    public ValidateOptionsResult Validate(string? name, PinqponqOpenApiOptions options)
    {
        var failures = new List<string>();

        if (options.BearerAuthentication.Enabled && string.IsNullOrWhiteSpace(options.BearerAuthentication.SchemeName))
        {
            failures.Add($"{nameof(PinqponqOpenApiOptions.BearerAuthentication)}.{nameof(BearerAuthenticationOptions.SchemeName)} is required when Bearer is enabled.");
        }

        if (options.ApiKeyAuthentication.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.ApiKeyAuthentication.SchemeName))
            {
                failures.Add($"{nameof(PinqponqOpenApiOptions.ApiKeyAuthentication)}.{nameof(ApiKeyAuthenticationOptions.SchemeName)} is required when the API key scheme is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.ApiKeyAuthentication.HeaderName))
            {
                failures.Add($"{nameof(PinqponqOpenApiOptions.ApiKeyAuthentication)}.{nameof(ApiKeyAuthenticationOptions.HeaderName)} is required when the API key scheme is enabled.");
            }
        }

        if (options.BearerAuthentication.Enabled
            && options.ApiKeyAuthentication.Enabled
            && string.Equals(options.BearerAuthentication.SchemeName, options.ApiKeyAuthentication.SchemeName, StringComparison.Ordinal))
        {
            failures.Add("The Bearer and API key schemes must have different SchemeName values.");
        }

        if (string.IsNullOrWhiteSpace(options.OpenApiRoutePattern)
            || !options.OpenApiRoutePattern.StartsWith('/')
            || !options.OpenApiRoutePattern.Contains(DocumentNamePlaceholder, StringComparison.Ordinal))
        {
            failures.Add($"{nameof(PinqponqOpenApiOptions.OpenApiRoutePattern)} must start with '/' and contain {DocumentNamePlaceholder}.");
        }

        if (string.IsNullOrWhiteSpace(options.ScalarRoutePrefix) || !options.ScalarRoutePrefix.StartsWith('/'))
        {
            failures.Add($"{nameof(PinqponqOpenApiOptions.ScalarRoutePrefix)} must start with '/'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
