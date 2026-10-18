namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// Turns one mail's copy and brand into the subject and bodies to send. The default is
/// <see cref="RevenueCatMailRenderer"/>; a product registers its own implementation to replace it.
/// </summary>
public interface IRevenueCatMailRenderer
{
    RevenueCatRenderedMail Render(RevenueCatMailRenderRequest request);
}

/// <summary>Everything a renderer needs for one mail.</summary>
/// <param name="MailEvent">The event the mail is for.</param>
/// <param name="Language">The language whose <paramref name="Text"/> was chosen.</param>
/// <param name="Text">The copy, with <c>{BrandName}</c> not yet replaced.</param>
/// <param name="Brand">The configured brand.</param>
/// <param name="EventOptions">The event's settings: button link and template override.</param>
/// <param name="Context">The RevenueCat event the mail was triggered by.</param>
public sealed record RevenueCatMailRenderRequest(
    RevenueCatMailEvent MailEvent,
    string Language,
    RevenueCatMailText Text,
    RevenueCatMailBrand Brand,
    RevenueCatMailEventOptions EventOptions,
    RevenueCatEventContext Context);

/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body.</param>
/// <param name="PlainTextBody">Plain-text version for clients that do not show HTML.</param>
public sealed record RevenueCatRenderedMail(string Subject, string HtmlBody, string PlainTextBody);
