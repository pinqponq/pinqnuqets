namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>The mail copy of one language, per event.</summary>
public sealed class RevenueCatMailTexts
{
    /// <summary>The text of each event that has one in this language.</summary>
    public IDictionary<RevenueCatMailEvent, RevenueCatMailText> Events { get; } =
        new Dictionary<RevenueCatMailEvent, RevenueCatMailText>();

    /// <summary>English texts for every event that name only the brand.</summary>
    public static RevenueCatMailTexts CreateEnglishDefaults() => new()
    {
        Events =
        {
            [RevenueCatMailEvent.InitialPurchase] = new RevenueCatMailText
            {
                Subject = "Your {BrandName} subscription is active",
                Preheader = "Thanks for subscribing to {BrandName}.",
                Title = "Thanks for subscribing",
                Paragraphs =
                {
                    "Your {BrandName} subscription is now active.",
                    "You can manage or cancel it at any time where you bought it.",
                },
            },
            [RevenueCatMailEvent.Renewal] = new RevenueCatMailText
            {
                Subject = "Your {BrandName} subscription was renewed",
                Preheader = "Your {BrandName} subscription continues.",
                Title = "Subscription renewed",
                Paragraphs = { "Your {BrandName} subscription was renewed for another period." },
            },
            [RevenueCatMailEvent.Cancellation] = new RevenueCatMailText
            {
                Subject = "Your {BrandName} subscription will not renew",
                Preheader = "You keep access until the end of the current period.",
                Title = "Subscription cancelled",
                Paragraphs =
                {
                    "Your {BrandName} subscription was cancelled and will not renew.",
                    "You keep access until the end of the current period.",
                },
            },
            [RevenueCatMailEvent.BillingIssue] = new RevenueCatMailText
            {
                Subject = "Payment problem with your {BrandName} subscription",
                Preheader = "Update your payment method to keep your subscription.",
                Title = "We could not renew your subscription",
                Paragraphs =
                {
                    "The latest payment for your {BrandName} subscription did not go through.",
                    "Update your payment method where you bought the subscription to keep it.",
                },
            },
            [RevenueCatMailEvent.Expiration] = new RevenueCatMailText
            {
                Subject = "Your {BrandName} subscription has ended",
                Preheader = "You can subscribe again at any time.",
                Title = "Subscription ended",
                Paragraphs =
                {
                    "Your {BrandName} subscription has ended.",
                    "You can subscribe again at any time.",
                },
            },
        },
    };
}

/// <summary>
/// The copy of one mail. <c>{BrandName}</c> in any value is replaced with
/// <see cref="RevenueCatMailBrand.Name"/>; every value is HTML-encoded when rendered.
/// </summary>
public sealed class RevenueCatMailText
{
    /// <summary>Subject line. Required.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Optional preview text that mail clients show next to the subject.</summary>
    public string? Preheader { get; set; }

    /// <summary>Heading of the mail. Required.</summary>
    public string Title { get; set; } = string.Empty;

    public IList<string> Paragraphs { get; set; } = new List<string>();

    /// <summary>Optional button label; see <see cref="RevenueCatMailEventOptions.CallToActionUrl"/>.</summary>
    public string? CallToActionLabel { get; set; }
}
