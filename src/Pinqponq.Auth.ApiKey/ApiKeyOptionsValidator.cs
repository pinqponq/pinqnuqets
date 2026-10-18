using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.ApiKey;

internal sealed class ApiKeyOptionsValidator(string optionsName) : IValidateOptions<ApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiKeyOptions options)
    {
        if (name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ApiKeyOptions.HeaderName)} is required for API key '{optionsName}'.");
        }

        return ValidateOptionsResult.Success;
    }
}
