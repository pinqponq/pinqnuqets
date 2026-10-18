namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// Names of the mail steps reported to <see cref="IRevenueCatWebhookObserver"/>, next to the
/// <see cref="Microsoft.Extensions.Logging.ILogger"/> output.
/// </summary>
public static class RevenueCatMailLogEvents
{
    public const string Sent = "webhook_revenuecat_mail_sent";
    public const string Skipped = "webhook_revenuecat_mail_skipped";
    public const string Failed = "webhook_revenuecat_mail_failed";
}

/// <summary>Keys of <see cref="RevenueCatWebhookLog.Data"/> in the mail steps.</summary>
public static class RevenueCatMailLogKeys
{
    public const string EventId = "eventId";
    public const string EventType = "eventType";
    public const string SubscriberId = "subscriberId";
    public const string MailEvent = "mailEvent";
    public const string Language = "language";
    public const string Reason = "reason";
    public const string ExceptionType = "exceptionType";
}

/// <summary>Values of <see cref="RevenueCatMailLogKeys.Reason"/> on a <see cref="RevenueCatMailLogEvents.Skipped"/> step.</summary>
public static class RevenueCatMailSkipReasons
{
    public const string EntitlementNotPremium = "entitlement_not_premium";
    public const string SubscriberUnresolved = "subscriber_unresolved";
    public const string RecipientUnresolved = "recipient_unresolved";
    public const string RecipientAddressInvalid = "recipient_address_invalid";
}
