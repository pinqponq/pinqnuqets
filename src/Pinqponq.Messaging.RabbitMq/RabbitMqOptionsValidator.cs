using Microsoft.Extensions.Options;

namespace Pinqponq.Messaging.RabbitMq;

/// <summary>Validates <see cref="RabbitMqOptions"/> at options bind / startup.</summary>
internal sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HostName))
        {
            return ValidateOptionsResult.Fail($"{nameof(RabbitMqOptions.HostName)} is required.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            return ValidateOptionsResult.Fail($"{nameof(RabbitMqOptions.Port)} must be between 1 and 65535.");
        }

        if (options.PrefetchCount < 1)
        {
            return ValidateOptionsResult.Fail($"{nameof(RabbitMqOptions.PrefetchCount)} must be at least 1.");
        }

        if (options.PublishRetryCount < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RabbitMqOptions.PublishRetryCount)} must not be negative.");
        }

        if (options.PublishRetryBaseDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RabbitMqOptions.PublishRetryBaseDelay)} must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}
