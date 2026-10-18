namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// Placeholders of the default layout and of <see cref="RevenueCatMailEventOptions.TemplateOverride"/>.
/// Values are HTML-encoded; the <c>*Html</c> placeholders hold markup built from encoded values.
/// </summary>
public static class RevenueCatMailTemplatePlaceholders
{
    public const string Language = "{Language}";
    public const string BrandName = "{BrandName}";
    public const string PrimaryColor = "{PrimaryColor}";
    public const string OnPrimaryColor = "{OnPrimaryColor}";
    public const string LogoUrl = "{LogoUrl}";
    public const string Subject = "{Subject}";
    public const string Preheader = "{Preheader}";
    public const string Title = "{Title}";
    public const string CallToActionLabel = "{CallToActionLabel}";
    public const string CallToActionUrl = "{CallToActionUrl}";
    public const string FooterText = "{FooterText}";

    /// <summary>The logo image, or the brand name as text when no logo is configured.</summary>
    public const string HeaderHtml = "{HeaderHtml}";

    /// <summary>One <c>&lt;p&gt;</c> per paragraph.</summary>
    public const string ParagraphsHtml = "{ParagraphsHtml}";

    /// <summary>The button, or nothing when the label or the link is missing.</summary>
    public const string CallToActionHtml = "{CallToActionHtml}";
}
