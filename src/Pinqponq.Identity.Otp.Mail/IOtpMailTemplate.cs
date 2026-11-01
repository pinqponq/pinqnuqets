namespace Pinqponq.Identity.Otp.Mail;

/// <summary>
/// Turns the values of one code mail into its HTML body. The default is
/// <see cref="OtpMailTemplate"/>; a product registers its own implementation for a different
/// layout. The values in <see cref="OtpMailModel"/> are not HTML-encoded, so an implementation
/// must encode them.
/// </summary>
public interface IOtpMailTemplate
{
    string Render(OtpMailModel model);
}

/// <summary>
/// Everything a template needs for one mail. The text values come from the chosen language's
/// <see cref="OtpMailTexts"/> with the placeholders already replaced; optional values are
/// <see langword="null"/> when they are not set.
/// </summary>
/// <param name="Language">The language whose texts were chosen.</param>
/// <param name="Code">The code.</param>
/// <param name="Minutes">The code lifetime in whole minutes, rounded up and at least 1.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="LogoUrl">The logo URL.</param>
/// <param name="CallToActionUrl">The button link, set only when the button label is set too.</param>
/// <param name="Colors">The configured colors.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Preheader">The preview text.</param>
/// <param name="Title">The heading.</param>
/// <param name="Greeting">The first line of the text.</param>
/// <param name="Body">The text below the code.</param>
/// <param name="LifetimeLabel">The label that says how long the code is valid.</param>
/// <param name="CallToActionLabel">The button label, set only when the button link is set too.</param>
/// <param name="Footer">The footer line.</param>
public sealed record OtpMailModel(
    string Language,
    string Code,
    int Minutes,
    string ProductName,
    string? LogoUrl,
    string? CallToActionUrl,
    OtpMailColors Colors,
    string Subject,
    string? Preheader,
    string Title,
    string? Greeting,
    string Body,
    string LifetimeLabel,
    string? CallToActionLabel,
    string Footer);
