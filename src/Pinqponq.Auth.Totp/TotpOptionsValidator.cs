using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Totp;

/// <summary>Validates <see cref="TotpOptions"/> at options bind / startup.</summary>
internal sealed class TotpOptionsValidator : IValidateOptions<TotpOptions>
{
    private const int MaxValidationWindow = 10;

    public ValidateOptionsResult Validate(string? name, TotpOptions options)
    {
        if (options.Digits is < 6 or > 8)
        {
            return ValidateOptionsResult.Fail($"{nameof(TotpOptions.Digits)} must be between 6 and 8.");
        }

        if (options.PeriodSeconds <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(TotpOptions.PeriodSeconds)} must be greater than zero.");
        }

        if (options.ValidationWindow is < 0 or > MaxValidationWindow)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TotpOptions.ValidationWindow)} must be between 0 and {MaxValidationWindow}.");
        }

        if (options.SecretByteLength < 16)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TotpOptions.SecretByteLength)} must be at least 16.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return ValidateOptionsResult.Fail($"{nameof(TotpOptions.Issuer)} is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
