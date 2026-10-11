namespace Pinqponq.Billing.RevenueCat;

/// <summary>Settings for <see cref="IRevenueCatEventHandler"/> and <see cref="IRevenueCatWebhookAuthenticator"/>.</summary>
public sealed class RevenueCatOptions
{
    /// <summary>Configuration section the options are conventionally bound from.</summary>
    public const string SectionName = "RevenueCat";

    /// <summary>
    /// The Authorization header value configured for the webhook in the RevenueCat dashboard.
    /// Incoming requests must carry exactly this value. Required.
    /// </summary>
    public string WebhookAuthHeader { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the entitlement that counts as premium. Must match the entitlement in the
    /// RevenueCat dashboard and in the mobile app. Required.
    /// </summary>
    public string PremiumEntitlementId { get; set; } = string.Empty;
}
