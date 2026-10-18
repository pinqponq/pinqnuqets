using Microsoft.Extensions.Options;

namespace Pinqponq.Push.Fcm;

/// <summary>Validates <see cref="FcmOptions"/> at startup.</summary>
public sealed class FcmOptionsValidator : IValidateOptions<FcmOptions>
{
    public ValidateOptionsResult Validate(string? name, FcmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.DataOnlyAndroidPriority))
        {
            return ValidateOptionsResult.Fail($"{nameof(FcmOptions.DataOnlyAndroidPriority)} value is invalid.");
        }

        if (!options.IsEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var hasCredentialPath = !string.IsNullOrWhiteSpace(options.CredentialPath);
        var hasCredentialJson = !string.IsNullOrWhiteSpace(options.CredentialJson);
        if (hasCredentialPath == hasCredentialJson)
        {
            return ValidateOptionsResult.Fail(
                $"Set exactly one of {nameof(FcmOptions.CredentialPath)} and {nameof(FcmOptions.CredentialJson)} when {nameof(FcmOptions.IsEnabled)} is true.");
        }

        if (hasCredentialPath)
        {
            var credentialFullPath = FcmCredentialLoader.ResolveCredentialPath(options.CredentialPath!);
            if (!File.Exists(credentialFullPath))
            {
                return ValidateOptionsResult.Fail(
                    $"Firebase credential file not found at '{credentialFullPath}'. Check {nameof(FcmOptions.CredentialPath)}.");
            }
        }

        return ValidateCredential(options);
    }

    private static ValidateOptionsResult ValidateCredential(FcmOptions options)
    {
        try
        {
            FcmCredentialLoader.Load(options);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException)
        {
            // ValidateOptionsResult carries only text, so the exception type and message stand in for the exception.
            return ValidateOptionsResult.Fail(
                $"The Firebase credential is not a valid service account JSON: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
