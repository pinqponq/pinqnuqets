using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Sso.Google;

/// <summary>Validates <see cref="GoogleAuthOptions"/> at options bind / startup.</summary>
internal sealed class GoogleAuthOptionsValidator : IValidateOptions<GoogleAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleAuthOptions options)
    {
        if (options.ClientIds.Count == 0
            || options.ClientIds.Any(static id => string.IsNullOrWhiteSpace(id)))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(GoogleAuthOptions.ClientIds)} must contain at least one non-empty client id.");
        }

        return ValidateOptionsResult.Success;
    }
}
