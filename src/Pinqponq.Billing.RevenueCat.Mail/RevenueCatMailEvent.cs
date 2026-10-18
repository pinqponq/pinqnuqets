namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>The RevenueCat events that can send a mail.</summary>
public enum RevenueCatMailEvent
{
    /// <summary><c>INITIAL_PURCHASE</c>: the first purchase of a subscription.</summary>
    InitialPurchase,

    /// <summary><c>RENEWAL</c>: a subscription period was renewed.</summary>
    Renewal,

    /// <summary><c>CANCELLATION</c>: the subscription will not renew; access stays until the period ends.</summary>
    Cancellation,

    /// <summary><c>BILLING_ISSUE</c>: a renewal payment failed.</summary>
    BillingIssue,

    /// <summary><c>EXPIRATION</c>: the subscription has ended.</summary>
    Expiration,
}

internal static class RevenueCatMailEventMapping
{
    public static bool TryMap(string? eventType, out RevenueCatMailEvent mailEvent)
    {
        switch (eventType)
        {
            case RevenueCatEventType.InitialPurchase:
                mailEvent = RevenueCatMailEvent.InitialPurchase;
                return true;
            case RevenueCatEventType.Renewal:
                mailEvent = RevenueCatMailEvent.Renewal;
                return true;
            case RevenueCatEventType.Cancellation:
                mailEvent = RevenueCatMailEvent.Cancellation;
                return true;
            case RevenueCatEventType.BillingIssue:
                mailEvent = RevenueCatMailEvent.BillingIssue;
                return true;
            case RevenueCatEventType.Expiration:
                mailEvent = RevenueCatMailEvent.Expiration;
                return true;
            default:
                mailEvent = default;
                return false;
        }
    }
}
