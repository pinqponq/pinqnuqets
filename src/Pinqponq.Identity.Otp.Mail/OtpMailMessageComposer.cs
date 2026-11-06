using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Otp.Mail;

/// <summary>
/// The <see cref="IOtpMessageComposer"/> this package registers: picks the recipient's language,
/// fills that language's texts, renders the HTML body through <see cref="IOtpMailTemplate"/> and
/// adds a plain-text version.
/// </summary>
public sealed class OtpMailMessageComposer : IOtpMessageComposer
{
    private readonly IOtpMailLanguageResolver _languageResolver;
    private readonly IOtpMailTemplate _mailTemplate;
    private readonly OtpMailOptions _mailOptions;
    private readonly OtpOptions _otpOptions;

    public OtpMailMessageComposer(
        IOtpMailLanguageResolver languageResolver,
        IOtpMailTemplate mailTemplate,
        IOptions<OtpMailOptions> mailOptions,
        IOptions<OtpOptions> otpOptions)
    {
        _languageResolver = languageResolver ?? throw new ArgumentNullException(nameof(languageResolver));
        _mailTemplate = mailTemplate ?? throw new ArgumentNullException(nameof(mailTemplate));
        ArgumentNullException.ThrowIfNull(mailOptions);
        ArgumentNullException.ThrowIfNull(otpOptions);
        _mailOptions = mailOptions.Value;
        _otpOptions = otpOptions.Value;
    }

    /// <inheritdoc />
    public async Task<OtpEmailContent> ComposeEmailAsync(OtpMessageContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestedLanguage = await _languageResolver
            .ResolveLanguageAsync(context.Recipient, cancellationToken)
            .ConfigureAwait(false);
        var model = BuildModel(context.Code, ChooseLanguage(requestedLanguage));
        var htmlBody = _mailTemplate.Render(model);
        var plainTextBody = BuildPlainText(model);
        return new OtpEmailContent
        {
            Subject = model.Subject,
            Body = htmlBody,
            IsBodyHtml = true,
            PlainTextBody = plainTextBody,
        };
    }

    private string ChooseLanguage(string? requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage) || !_mailOptions.Texts.ContainsKey(requestedLanguage))
        {
            return _mailOptions.DefaultLanguage;
        }

        return requestedLanguage;
    }

    private OtpMailModel BuildModel(string code, string language)
    {
        var texts = _mailOptions.Texts[language];
        var minutes = LifetimeMinutes();
        var lifetimeLabel = minutes == 1 && !string.IsNullOrWhiteSpace(texts.LifetimeLabelOneMinute)
            ? texts.LifetimeLabelOneMinute
            : texts.LifetimeLabel;
        var callToActionLabel = OptionalText(texts.CallToActionLabel, code, minutes);
        var callToActionUrl = OptionalValue(_mailOptions.CallToActionUrl);
        var hasCallToAction = callToActionLabel is not null && callToActionUrl is not null;
        var footer = OptionalText(texts.Footer, code, minutes) ?? _mailOptions.ProductName;

        return new OtpMailModel(
            language,
            code,
            minutes,
            _mailOptions.ProductName,
            OptionalValue(_mailOptions.LogoUrl),
            hasCallToAction ? callToActionUrl : null,
            _mailOptions.Colors,
            FillPlaceholders(texts.Subject, code, minutes),
            OptionalText(texts.Preheader, code, minutes),
            FillPlaceholders(texts.Title, code, minutes),
            OptionalText(texts.Greeting, code, minutes),
            FillPlaceholders(texts.Body, code, minutes),
            FillPlaceholders(lifetimeLabel, code, minutes),
            hasCallToAction ? callToActionLabel : null,
            footer);
    }

    private int LifetimeMinutes() => Math.Max(1, (int)Math.Ceiling(_otpOptions.Ttl.TotalMinutes));

    private string? OptionalText(string? text, string code, int minutes) =>
        string.IsNullOrWhiteSpace(text) ? null : FillPlaceholders(text, code, minutes);

    private static string? OptionalValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private string FillPlaceholders(string text, string code, int minutes) =>
        text.Replace(OtpMailTextPlaceholders.ProductName, _mailOptions.ProductName, StringComparison.Ordinal)
            .Replace(OtpMailTextPlaceholders.Code, code, StringComparison.Ordinal)
            .Replace(OtpMailTextPlaceholders.Minutes, minutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string BuildPlainText(OtpMailModel model)
    {
        var builder = new StringBuilder();
        builder.Append(model.Title).Append("\n\n");
        builder.Append(model.Code).Append('\n');
        builder.Append(model.LifetimeLabel).Append("\n\n");
        if (model.Greeting is not null)
        {
            builder.Append(model.Greeting).Append("\n\n");
        }

        builder.Append(model.Body).Append("\n\n");
        if (model.CallToActionUrl is not null)
        {
            builder.Append(model.CallToActionLabel).Append(": ").Append(model.CallToActionUrl).Append("\n\n");
        }

        builder.Append("-- \n").Append(model.Footer).Append('\n');
        return builder.ToString();
    }
}
