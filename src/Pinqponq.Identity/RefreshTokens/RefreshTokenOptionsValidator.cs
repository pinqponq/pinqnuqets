using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.RefreshTokens;

/// <summary>Validates <see cref="RefreshTokenOptions"/> at options bind / startup.</summary>
internal sealed class RefreshTokenOptionsValidator : IValidateOptions<RefreshTokenOptions>
{
    private const int MinimumTokenByteLength = 32;

    public ValidateOptionsResult Validate(string? name, RefreshTokenOptions options)
    {
        if (options.Lifetime <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(RefreshTokenOptions.Lifetime)} must be positive.");
        }

        if (options.TokenByteLength < MinimumTokenByteLength)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RefreshTokenOptions.TokenByteLength)} must be at least {MinimumTokenByteLength}.");
        }

        if (options.ReuseDetectionGrace < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RefreshTokenOptions.ReuseDetectionGrace)} must be non-negative.");
        }

        return ValidateOptionsResult.Success;
    }
}
