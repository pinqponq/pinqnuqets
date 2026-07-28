using Microsoft.Extensions.Options;

namespace Pinqponq.Database.Postgres;

/// <summary>Validates <see cref="PostgresOptions"/> at options bind / startup.</summary>
internal sealed class PostgresOptionsValidator : IValidateOptions<PostgresOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgresOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"{nameof(PostgresOptions.ConnectionString)} is required.");
        }

        if (options.RetryCount < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(PostgresOptions.RetryCount)} must not be negative.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(PostgresOptions.RetryBaseDelay)} must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}
