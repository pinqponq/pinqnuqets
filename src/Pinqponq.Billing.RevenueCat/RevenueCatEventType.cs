namespace Pinqponq.Billing.RevenueCat;

/// <summary>String constants for the RevenueCat webhook <c>type</c> field.</summary>
public static class RevenueCatEventType
{
    public const string InitialPurchase = "INITIAL_PURCHASE";
    public const string Renewal = "RENEWAL";
    public const string ProductChange = "PRODUCT_CHANGE";
    public const string Cancellation = "CANCELLATION";
    public const string Uncancellation = "UNCANCELLATION";
    public const string NonRenewingPurchase = "NON_RENEWING_PURCHASE";
    public const string Expiration = "EXPIRATION";
    public const string BillingIssue = "BILLING_ISSUE";
    public const string SubscriberAlias = "SUBSCRIBER_ALIAS";
    public const string Transfer = "TRANSFER";
    public const string Test = "TEST";
}
