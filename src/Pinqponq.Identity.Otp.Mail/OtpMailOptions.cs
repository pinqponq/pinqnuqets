namespace Pinqponq.Identity.Otp.Mail;

public sealed class OtpMailOptions
{
    private const string EnglishLanguage = "en";

    /// <summary>Product name. It replaces <c>{ProductName}</c> in the texts, is the alternative text of the logo, and is the footer when no footer text is set. Required.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Optional absolute http(s) URL of the logo shown in the header. The header is hidden when it is empty.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Optional absolute http(s) URL the button links to. The button is shown only when this and
    /// <see cref="OtpMailTexts.CallToActionLabel"/> are both set.
    /// </summary>
    public string? CallToActionUrl { get; set; }

    /// <summary>The colors of the default layout.</summary>
    public OtpMailColors Colors { get; set; } = new();

    /// <summary>
    /// The language used when the recipient has none or has one without texts. Texts for this
    /// language are required. Defaults to <c>en</c>.
    /// </summary>
    public string DefaultLanguage { get; set; } = EnglishLanguage;

    /// <summary>
    /// The mail texts per language (case-insensitive key such as <c>en</c> or <c>de</c>). Starts
    /// with English texts that name only the product.
    /// </summary>
    public IDictionary<string, OtpMailTexts> Texts { get; } =
        new Dictionary<string, OtpMailTexts>(StringComparer.OrdinalIgnoreCase)
        {
            [EnglishLanguage] = OtpMailTexts.CreateEnglishDefaults(),
        };
}

/// <summary>The colors of the default layout, each as <c>#rgb</c> or <c>#rrggbb</c>.</summary>
public sealed class OtpMailColors
{
    /// <summary>Background around the mail. Defaults to light gray.</summary>
    public string PageBackground { get; set; } = "#f2f2f2";

    /// <summary>Background of the mail itself. Defaults to white.</summary>
    public string CardBackground { get; set; } = "#ffffff";

    /// <summary>Background of the header that holds the logo. Defaults to dark gray.</summary>
    public string HeaderBackground { get; set; } = "#333333";

    /// <summary>Color of the code and the button. Defaults to blue.</summary>
    public string Accent { get; set; } = "#1a73e8";

    /// <summary>Color of the button label shown on <see cref="Accent"/>. Defaults to white.</summary>
    public string OnAccent { get; set; } = "#ffffff";

    /// <summary>Background of the panel that holds the code. Defaults to light blue.</summary>
    public string CodeBackground { get; set; } = "#eef3fd";

    /// <summary>Color of the title. Defaults to near black.</summary>
    public string Title { get; set; } = "#222222";

    /// <summary>Color of the greeting and the body text. Defaults to dark gray.</summary>
    public string Text { get; set; } = "#444444";

    /// <summary>Color of the lifetime label and the footer. Defaults to gray.</summary>
    public string Muted { get; set; } = "#888888";
}
