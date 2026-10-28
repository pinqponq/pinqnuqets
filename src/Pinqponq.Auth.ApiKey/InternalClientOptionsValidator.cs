using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.ApiKey;

internal sealed class InternalClientOptionsValidator : IValidateOptions<InternalClientOptions>
{
    public ValidateOptionsResult Validate(string? name, InternalClientOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{nameof(InternalClientOptions.BaseUrl)} must be an absolute http or https URL in section '{name}'.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{nameof(InternalClientOptions.ApiKey)} is required in section '{name}'.");
        }

        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            failures.Add($"{nameof(InternalClientOptions.HeaderName)} is required in section '{name}'.");
        }
        else if (!IsValidHeaderName(options.HeaderName))
        {
            failures.Add($"{nameof(InternalClientOptions.HeaderName)} is not a valid HTTP header name in section '{name}'.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"{nameof(InternalClientOptions.TimeoutSeconds)} must be positive in section '{name}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidHeaderName(string headerName)
    {
        using var request = new HttpRequestMessage();
        return request.Headers.TryAddWithoutValidation(headerName, "value");
    }
}
