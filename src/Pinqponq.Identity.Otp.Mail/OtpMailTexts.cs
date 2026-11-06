namespace Pinqponq.Identity.Otp.Mail;

/// <summary>
/// The texts of the code mail in one language. The placeholders in
/// <see cref="OtpMailTextPlaceholders"/> are replaced in every value; every value is HTML-encoded
/// when rendered.
/// </summary>
public sealed class OtpMailTexts
{
    /// <summary>Subject line. Required.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Optional preview text that mail clients show next to the subject.</summary>
    public string? Preheader { get; set; }

    /// <summary>Heading of the mail. Required.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional first line of the text, for example "Hello,".</summary>
    public string? Greeting { get; set; }

    /// <summary>The text below the code. Required.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The label above the code that says how long it is valid. Required.</summary>
    public string LifetimeLabel { get; set; } = string.Empty;

    /// <summary>Optional label used instead of <see cref="LifetimeLabel"/> when the code is valid for exactly one minute.</summary>
    public string? LifetimeLabelOneMinute { get; set; }

    /// <summary>Optional button label; see <see cref="OtpMailOptions.CallToActionUrl"/>.</summary>
    public string? CallToActionLabel { get; set; }

    /// <summary>Optional footer line. Without it the footer shows <see cref="OtpMailOptions.ProductName"/>.</summary>
    public string? Footer { get; set; }

    /// <summary>English texts that name only the product.</summary>
    public static OtpMailTexts CreateEnglishDefaults() => new()
    {
        Subject = "Your {ProductName} verification code",
        Preheader = "Your verification code is {Code}.",
        Title = "Your verification code",
        Greeting = "Hello,",
        Body = "Enter this code in {ProductName} to continue. If you did not ask for it, you can ignore this email.",
        LifetimeLabel = "Valid for {Minutes} minutes",
        LifetimeLabelOneMinute = "Valid for 1 minute",
        CallToActionLabel = "Open {ProductName}",
        Footer = "This email was sent automatically by {ProductName}.",
    };
}
