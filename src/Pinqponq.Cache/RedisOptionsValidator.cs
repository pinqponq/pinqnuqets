using Microsoft.Extensions.Options;

namespace Pinqponq.Cache;

/// <summary>Validates <see cref="RedisOptions"/> at options bind / startup.</summary>
internal sealed class RedisOptionsValidator : IValidateOptions<RedisOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"{nameof(RedisOptions.ConnectionString)} is required.");
        }

        if (options.DefaultTtl is { } ttl && ttl <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RedisOptions.DefaultTtl)} must be positive when set.");
        }

        return ValidateOptionsResult.Success;
    }
}
