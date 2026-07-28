using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Otp;

/// <summary>Validates <see cref="OtpOptions"/> at options bind / startup.</summary>
public sealed class OtpOptionsValidator : IValidateOptions<OtpOptions>
{
    public ValidateOptionsResult Validate(string? name, OtpOptions options)
    {
        if (options.CodeLength is < 4 or > 12)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OtpOptions.CodeLength)} must be between 4 and 12.");
        }

        if (options.Ttl <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(OtpOptions.Ttl)} must be positive.");
        }

        if (options.MaxAttempts <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(OtpOptions.MaxAttempts)} must be greater than zero.");
        }

        if (options.MinSendInterval < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OtpOptions.MinSendInterval)} must be greater than or equal to zero.");
        }

        if (string.IsNullOrWhiteSpace(options.HashPepper)
            || options.HashPepper.Length < OtpOptions.MinimumPepperLength)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(OtpOptions.HashPepper)} must be at least {OtpOptions.MinimumPepperLength} characters.");
        }

        return ValidateOptionsResult.Success;
    }
}
