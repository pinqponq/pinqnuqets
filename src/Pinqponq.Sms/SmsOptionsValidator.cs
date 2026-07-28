using Microsoft.Extensions.Options;

namespace Pinqponq.Sms;

/// <summary>Validates <see cref="SmsOptions"/> at options bind / startup.</summary>
public sealed class SmsOptionsValidator : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        if (options.RetryCount < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(SmsOptions.RetryCount)} must not be negative.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(SmsOptions.RetryBaseDelay)} must be positive.");
        }

        if (options.HttpTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(SmsOptions.HttpTimeout)} must be positive.");
        }

        if (!Enum.IsDefined(options.Transport))
        {
            return ValidateOptionsResult.Fail($"{nameof(SmsOptions.Transport)} value is invalid.");
        }

        if (options.Transport == SmsTransport.RestV2)
        {
            var url = string.IsNullOrWhiteSpace(options.ApiUrl)
                ? SmsOptions.DefaultRestV2ApiUrl
                : options.ApiUrl;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var restUri)
                || !string.Equals(restUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(SmsOptions.ApiUrl)} must be an absolute HTTPS URL for RestV2.");
            }

            if (string.IsNullOrWhiteSpace(options.UserCode))
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(SmsOptions.UserCode)} is required for RestV2.");
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(SmsOptions.Password)} is required for RestV2.");
            }

            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.ApiUrl))
        {
            if (!options.AllowNoOp)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(SmsOptions.ApiUrl)} is required when {nameof(SmsOptions.AllowNoOp)} is false.");
            }

            return ValidateOptionsResult.Success;
        }

        if (!Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SmsOptions.ApiUrl)} must be an absolute HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(options.UserCode))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SmsOptions.UserCode)} is required when {nameof(SmsOptions.ApiUrl)} is set.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SmsOptions.Password)} is required when {nameof(SmsOptions.ApiUrl)} is set.");
        }

        return ValidateOptionsResult.Success;
    }
}
