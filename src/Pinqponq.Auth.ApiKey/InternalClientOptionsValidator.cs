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

        if (!string.IsNullOrWhiteSpace(options.ApiKey) && !IsValidHeader("X-Check", options.ApiKey))
        {
            failures.Add($"{nameof(InternalClientOptions.ApiKey)} is not a valid HTTP header value in section '{name}'.");
        }

        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            failures.Add($"{nameof(InternalClientOptions.HeaderName)} is required in section '{name}'.");
        }
        else if (!IsValidHeader(options.HeaderName, "value"))
        {
            failures.Add($"{nameof(InternalClientOptions.HeaderName)} is not a valid HTTP header name in section '{name}'.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"{nameof(InternalClientOptions.TimeoutSeconds)} must be positive in section '{name}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidHeader(string headerName, string value)
    {
        using var request = new HttpRequestMessage();
        try
        {
            request.Headers.Add(headerName, value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
