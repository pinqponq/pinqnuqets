using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat;

/// <summary>Validates <see cref="RevenueCatOptions"/> at options bind / startup.</summary>
public sealed class RevenueCatOptionsValidator : IValidateOptions<RevenueCatOptions>
{
    public ValidateOptionsResult Validate(string? name, RevenueCatOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookAuthHeader))
        {
            return ValidateOptionsResult.Fail($"{nameof(RevenueCatOptions.WebhookAuthHeader)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.PremiumEntitlementId))
        {
            return ValidateOptionsResult.Fail($"{nameof(RevenueCatOptions.PremiumEntitlementId)} is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
