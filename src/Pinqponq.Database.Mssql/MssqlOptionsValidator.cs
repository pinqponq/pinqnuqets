using Microsoft.Extensions.Options;

namespace Pinqponq.Database.Mssql;

/// <summary>Validates <see cref="MssqlOptions"/> at options bind / startup.</summary>
internal sealed class MssqlOptionsValidator : IValidateOptions<MssqlOptions>
{
    public ValidateOptionsResult Validate(string? name, MssqlOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"{nameof(MssqlOptions.ConnectionString)} is required.");
        }

        if (options.RetryCount < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(MssqlOptions.RetryCount)} must not be negative.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(MssqlOptions.RetryBaseDelay)} must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}
