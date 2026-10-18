using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// Default <see cref="IRevenueCatMailRenderer"/>: a single-column branded HTML layout (header
/// with logo or brand name, title, paragraphs, optional button, footer) and a plain-text version.
/// Every text and brand value is HTML-encoded.
/// </summary>
public sealed partial class RevenueCatMailRenderer : IRevenueCatMailRenderer
{
    public RevenueCatRenderedMail Render(RevenueCatMailRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var brandName = request.Brand.Name;
        var subject = ApplyBrand(request.Text.Subject, brandName);
        var preheader = ApplyBrand(request.Text.Preheader ?? string.Empty, brandName);
        var title = ApplyBrand(request.Text.Title, brandName);
        var paragraphs = request.Text.Paragraphs.Select(paragraph => ApplyBrand(paragraph, brandName)).ToList();
        var footerText = ApplyBrand(request.Brand.FooterText ?? brandName, brandName);
        var callToActionLabel = ApplyBrand(request.Text.CallToActionLabel ?? string.Empty, brandName);
        var callToActionUrl = request.EventOptions.CallToActionUrl ?? string.Empty;
        var hasCallToAction = !string.IsNullOrWhiteSpace(callToActionLabel) && !string.IsNullOrWhiteSpace(callToActionUrl);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RevenueCatMailTemplatePlaceholders.Language] = Encode(request.Language),
            [RevenueCatMailTemplatePlaceholders.BrandName] = Encode(brandName),
            [RevenueCatMailTemplatePlaceholders.PrimaryColor] = Encode(request.Brand.PrimaryColor),
            [RevenueCatMailTemplatePlaceholders.LogoUrl] = Encode(request.Brand.LogoUrl ?? string.Empty),
            [RevenueCatMailTemplatePlaceholders.Subject] = Encode(subject),
            [RevenueCatMailTemplatePlaceholders.Preheader] = Encode(preheader),
            [RevenueCatMailTemplatePlaceholders.Title] = Encode(title),
            [RevenueCatMailTemplatePlaceholders.CallToActionLabel] = Encode(callToActionLabel),
            [RevenueCatMailTemplatePlaceholders.CallToActionUrl] = Encode(callToActionUrl),
            [RevenueCatMailTemplatePlaceholders.FooterText] = Encode(footerText),
            [RevenueCatMailTemplatePlaceholders.HeaderHtml] = BuildHeaderHtml(request.Brand),
            [RevenueCatMailTemplatePlaceholders.ParagraphsHtml] = BuildParagraphsHtml(paragraphs),
            [RevenueCatMailTemplatePlaceholders.CallToActionHtml] = hasCallToAction
                ? BuildCallToActionHtml(callToActionLabel, callToActionUrl, request.Brand.PrimaryColor)
                : string.Empty,
        };

        var template = request.EventOptions.TemplateOverride ?? DefaultTemplate;
        var htmlBody = FillPlaceholders(template, values);
        var plainTextBody = BuildPlainText(title, paragraphs, hasCallToAction, callToActionLabel, callToActionUrl, footerText);
        return new RevenueCatRenderedMail(subject, htmlBody, plainTextBody);
    }

    private static string ApplyBrand(string text, string brandName) =>
        text.Replace(RevenueCatMailTemplatePlaceholders.BrandName, brandName, StringComparison.Ordinal);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string FillPlaceholders(string template, Dictionary<string, string> values) =>
        PlaceholderPattern().Replace(
            template,
            match => values.TryGetValue(match.Value, out var value) ? value : match.Value);

    private static string BuildHeaderHtml(RevenueCatMailBrand brand)
    {
        var brandName = Encode(brand.Name);
        if (string.IsNullOrWhiteSpace(brand.LogoUrl))
        {
            return $"""<span style="font-size: 22px; font-weight: bold; color: #ffffff;">{brandName}</span>""";
        }

        return $"""<img src="{Encode(brand.LogoUrl)}" alt="{brandName}" height="48" style="display: block; margin: 0 auto; border: 0; height: 48px; width: auto;" />""";
    }

    private static string BuildParagraphsHtml(List<string> paragraphs)
    {
        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            builder.Append("""<p style="margin: 0 0 16px; font-size: 15px; line-height: 1.6; color: #444444;">""")
                .Append(Encode(paragraph))
                .Append("</p>");
        }

        return builder.ToString();
    }

    private static string BuildCallToActionHtml(string label, string url, string primaryColor) =>
        $"""<p style="margin: 24px 0 8px; text-align: center;"><a href="{Encode(url)}" style="display: inline-block; background-color: {Encode(primaryColor)}; color: #ffffff; font-size: 15px; font-weight: bold; text-decoration: none; padding: 12px 28px; border-radius: 6px;">{Encode(label)}</a></p>""";

    private static string BuildPlainText(
        string title,
        List<string> paragraphs,
        bool hasCallToAction,
        string callToActionLabel,
        string callToActionUrl,
        string footerText)
    {
        var builder = new StringBuilder();
        builder.Append(title).Append("\n\n");
        foreach (var paragraph in paragraphs)
        {
            builder.Append(paragraph).Append("\n\n");
        }

        if (hasCallToAction)
        {
            builder.Append(callToActionLabel).Append(": ").Append(callToActionUrl).Append("\n\n");
        }

        builder.Append("-- \n").Append(footerText).Append('\n');
        return builder.ToString();
    }

    [GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial Regex PlaceholderPattern();

    private const string DefaultTemplate =
        """
        <!DOCTYPE html>
        <html lang="{Language}">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>{Subject}</title>
        </head>
        <body style="margin: 0; padding: 24px 12px; background-color: #f2f2f2; font-family: Helvetica, Arial, sans-serif;">
            <div style="display: none; max-height: 0; overflow: hidden; opacity: 0;">{Preheader}</div>
            <div style="max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden;">
                <div style="background-color: {PrimaryColor}; padding: 24px; text-align: center;">{HeaderHtml}</div>
                <div style="padding: 32px 32px 16px;">
                    <h1 style="margin: 0 0 20px; font-size: 22px; color: #222222;">{Title}</h1>
                    {ParagraphsHtml}
                    {CallToActionHtml}
                </div>
                <div style="padding: 16px 32px 24px; font-size: 12px; color: #888888; text-align: center;">{FooterText}</div>
            </div>
        </body>
        </html>
        """;
}
