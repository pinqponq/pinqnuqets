using Microsoft.Extensions.Options;

namespace Pinqponq.Mail;

/// <summary>Validates <see cref="SmtpOptions"/> at options bind / startup.</summary>
internal sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            return ValidateOptionsResult.Fail($"{nameof(SmtpOptions.SmtpHost)} is required.");
        }

        if (options.SmtpPort is <= 0 or > 65535)
        {
            return ValidateOptionsResult.Fail($"{nameof(SmtpOptions.SmtpPort)} must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.FromEmail))
        {
            return ValidateOptionsResult.Fail($"{nameof(SmtpOptions.FromEmail)} is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
