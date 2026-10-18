namespace Pinqponq.Billing.RevenueCat.Mail;

public sealed class RevenueCatMailOptions
{
    private const string EnglishLanguage = "en";

    /// <summary>The brand shown in every mail. <see cref="RevenueCatMailBrand.Name"/> is required.</summary>
    public RevenueCatMailBrand Brand { get; } = new();

    /// <summary>
    /// The language used when the recipient has none or has one without texts. Texts for this
    /// language are required for every enabled event. Defaults to <c>en</c>.
    /// </summary>
    public string DefaultLanguage { get; set; } = EnglishLanguage;

    /// <summary>
    /// When true (the default), only events that carry
    /// <see cref="RevenueCatOptions.PremiumEntitlementId"/> send a mail.
    /// </summary>
    public bool RequirePremiumEntitlement { get; set; } = true;

    /// <summary>Per-event settings. Only <see cref="RevenueCatMailEvent.InitialPurchase"/> is enabled by default.</summary>
    public IDictionary<RevenueCatMailEvent, RevenueCatMailEventOptions> Events { get; } = CreateDefaultEvents();

    /// <summary>
    /// The mail copy per language (case-insensitive key such as <c>en</c> or <c>tr</c>). Starts
    /// with English texts for every event that name only the brand.
    /// </summary>
    public IDictionary<string, RevenueCatMailTexts> Texts { get; } =
        new Dictionary<string, RevenueCatMailTexts>(StringComparer.OrdinalIgnoreCase)
        {
            [EnglishLanguage] = RevenueCatMailTexts.CreateEnglishDefaults(),
        };

    private static Dictionary<RevenueCatMailEvent, RevenueCatMailEventOptions> CreateDefaultEvents()
    {
        var events = Enum.GetValues<RevenueCatMailEvent>()
            .ToDictionary(mailEvent => mailEvent, _ => new RevenueCatMailEventOptions());
        events[RevenueCatMailEvent.InitialPurchase].Enabled = true;
        return events;
    }
}

/// <summary>The brand values the default mail layout shows.</summary>
public sealed class RevenueCatMailBrand
{
    /// <summary>Product name; replaces <c>{BrandName}</c> in the texts. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional absolute http(s) URL of the logo shown in the header. Without it the header shows <see cref="Name"/>.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Header and button color as <c>#rgb</c> or <c>#rrggbb</c>. Defaults to a neutral dark gray.</summary>
    public string PrimaryColor { get; set; } = "#333333";

    /// <summary>Color of the brand name and the button label shown on <see cref="PrimaryColor"/>, as <c>#rgb</c> or <c>#rrggbb</c>. Defaults to white.</summary>
    public string OnPrimaryColor { get; set; } = "#ffffff";

    /// <summary>Optional footer line; <c>{BrandName}</c> is replaced. Without it the footer shows <see cref="Name"/>.</summary>
    public string? FooterText { get; set; }
}

public sealed class RevenueCatMailEventOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional absolute http(s) URL the button links to. The button is shown only when this
    /// and <see cref="RevenueCatMailText.CallToActionLabel"/> are both set.
    /// </summary>
    public string? CallToActionUrl { get; set; }

    /// <summary>
    /// Optional full HTML template that replaces the default layout for this event. Placeholders
    /// are listed in <see cref="RevenueCatMailTemplatePlaceholders"/>; every value is HTML-encoded.
    /// </summary>
    public string? TemplateOverride { get; set; }
}
