using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat.Mail;

public sealed partial class RevenueCatMailOptionsValidator : IValidateOptions<RevenueCatMailOptions>
{
    public ValidateOptionsResult Validate(string? name, RevenueCatMailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateBrand(options.Brand, failures);
        ValidateTexts(options, failures);
        ValidateEvents(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateBrand(RevenueCatMailBrand brand, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(brand.Name))
        {
            failures.Add($"{nameof(RevenueCatMailOptions.Brand)}.{nameof(RevenueCatMailBrand.Name)} is required.");
        }

        ValidateColor(brand.PrimaryColor, nameof(RevenueCatMailBrand.PrimaryColor), failures);
        ValidateColor(brand.OnPrimaryColor, nameof(RevenueCatMailBrand.OnPrimaryColor), failures);

        if (brand.LogoUrl is not null && !IsAbsoluteHttpUrl(brand.LogoUrl))
        {
            failures.Add($"{nameof(RevenueCatMailOptions.Brand)}.{nameof(RevenueCatMailBrand.LogoUrl)} must be an absolute http or https URL.");
        }
    }

    private static void ValidateColor(string? color, string propertyName, List<string> failures)
    {
        if (color is null || !HexColorPattern().IsMatch(color))
        {
            failures.Add($"{nameof(RevenueCatMailOptions.Brand)}.{propertyName} must be a #rgb or #rrggbb color.");
        }
    }

    private static void ValidateTexts(RevenueCatMailOptions options, List<string> failures)
    {
        foreach (var (language, texts) in options.Texts)
        {
            foreach (var (mailEvent, text) in texts.Events)
            {
                var hasRequiredValues = !string.IsNullOrWhiteSpace(text.Subject) && !string.IsNullOrWhiteSpace(text.Title);
                if (!hasRequiredValues)
                {
                    failures.Add($"{nameof(RevenueCatMailOptions.Texts)}[{language}] {mailEvent} needs a {nameof(RevenueCatMailText.Subject)} and a {nameof(RevenueCatMailText.Title)}.");
                }

                var hasValidParagraphs = text.Paragraphs is not null
                    && text.Paragraphs.All(paragraph => !string.IsNullOrWhiteSpace(paragraph));
                if (!hasValidParagraphs)
                {
                    failures.Add($"{nameof(RevenueCatMailOptions.Texts)}[{language}] {mailEvent} {nameof(RevenueCatMailText.Paragraphs)} must be a list without empty entries.");
                }
            }
        }
    }

    private static void ValidateEvents(RevenueCatMailOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultLanguage))
        {
            failures.Add($"{nameof(RevenueCatMailOptions.DefaultLanguage)} is required.");
            return;
        }

        options.Texts.TryGetValue(options.DefaultLanguage, out var defaultTexts);
        foreach (var (mailEvent, eventOptions) in options.Events)
        {
            if (!eventOptions.Enabled)
            {
                continue;
            }

            if (defaultTexts is null || !defaultTexts.Events.ContainsKey(mailEvent))
            {
                failures.Add($"{mailEvent} is enabled but has no text for the default language '{options.DefaultLanguage}'.");
            }

            if (eventOptions.CallToActionUrl is not null && !IsAbsoluteHttpUrl(eventOptions.CallToActionUrl))
            {
                failures.Add($"{nameof(RevenueCatMailOptions.Events)}[{mailEvent}].{nameof(RevenueCatMailEventOptions.CallToActionUrl)} must be an absolute http or https URL.");
            }
        }
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColorPattern();
}
