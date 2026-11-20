using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Sso.Firebase;

/// <summary>Validates <see cref="FirebaseAuthOptions"/> when the host starts.</summary>
internal sealed class FirebaseAuthOptionsValidator : IValidateOptions<FirebaseAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, FirebaseAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectId))
        {
            return ValidateOptionsResult.Fail($"{nameof(FirebaseAuthOptions.ProjectId)} is required.");
        }

        if (options.AllowedSignInProviders.Any(static provider => string.IsNullOrWhiteSpace(provider)))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(FirebaseAuthOptions.AllowedSignInProviders)} must not contain empty entries.");
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(FirebaseAuthOptions.ClockSkew)} must not be negative.");
        }

        if (options.KeysCacheDurationWithoutMaxAge <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(FirebaseAuthOptions.KeysCacheDurationWithoutMaxAge)} must be positive.");
        }

        if (options.MinimumUnknownKeyRefreshInterval < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(FirebaseAuthOptions.MinimumUnknownKeyRefreshInterval)} must not be negative.");
        }

        var isHttpsAddress = options.PublicKeysUri is { IsAbsoluteUri: true }
            && options.PublicKeysUri.Scheme == Uri.UriSchemeHttps;
        if (!isHttpsAddress)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(FirebaseAuthOptions.PublicKeysUri)} must be an absolute HTTPS address.");
        }

        return ValidateOptionsResult.Success;
    }
}
