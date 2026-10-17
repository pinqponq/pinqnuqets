namespace Pinqponq.Billing.RevenueCat;

/// <summary>Verifies the Authorization header RevenueCat sends with every webhook call.</summary>
public interface IRevenueCatWebhookAuthenticator
{
    /// <summary>
    /// True when <paramref name="authorizationHeader"/> equals the configured
    /// <see cref="RevenueCatOptions.WebhookAuthHeader"/>. A missing header, or an unconfigured
    /// secret, is never authorized.
    /// </summary>
    bool IsAuthorized(string? authorizationHeader);
}
