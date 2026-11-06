using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Otp.Mail;

public sealed partial class OtpMailOptionsValidator : IValidateOptions<OtpMailOptions>
{
    public ValidateOptionsResult Validate(string? name, OtpMailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateProduct(options, failures);
        ValidateColors(options.Colors, failures);
        ValidateTexts(options, failures);
        ValidateDefaultLanguage(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateProduct(OtpMailOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.ProductName))
        {
            failures.Add($"{nameof(OtpMailOptions.ProductName)} is required.");
        }

        ValidateUrl(options.LogoUrl, nameof(OtpMailOptions.LogoUrl), failures);
        ValidateUrl(options.CallToActionUrl, nameof(OtpMailOptions.CallToActionUrl), failures);
    }

    private static void ValidateUrl(string? url, string propertyName, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(url) && !IsAbsoluteHttpUrl(url))
        {
            failures.Add($"{propertyName} must be an absolute http or https URL.");
        }
    }

    private static void ValidateColors(OtpMailColors? colors, List<string> failures)
    {
        if (colors is null)
        {
            failures.Add($"{nameof(OtpMailOptions.Colors)} is required.");
            return;
        }

        ValidateColor(colors.PageBackground, nameof(OtpMailColors.PageBackground), failures);
        ValidateColor(colors.CardBackground, nameof(OtpMailColors.CardBackground), failures);
        ValidateColor(colors.HeaderBackground, nameof(OtpMailColors.HeaderBackground), failures);
        ValidateColor(colors.Accent, nameof(OtpMailColors.Accent), failures);
        ValidateColor(colors.OnAccent, nameof(OtpMailColors.OnAccent), failures);
        ValidateColor(colors.CodeBackground, nameof(OtpMailColors.CodeBackground), failures);
        ValidateColor(colors.Title, nameof(OtpMailColors.Title), failures);
        ValidateColor(colors.Text, nameof(OtpMailColors.Text), failures);
        ValidateColor(colors.Muted, nameof(OtpMailColors.Muted), failures);
    }

    private static void ValidateColor(string? color, string propertyName, List<string> failures)
    {
        if (color is null || !HexColorPattern().IsMatch(color))
        {
            failures.Add($"{nameof(OtpMailOptions.Colors)}.{propertyName} must be a #rgb or #rrggbb color.");
        }
    }

    private static void ValidateTexts(OtpMailOptions options, List<string> failures)
    {
        foreach (var (language, texts) in options.Texts)
        {
            if (texts is null)
            {
                failures.Add($"{nameof(OtpMailOptions.Texts)}[{language}] is empty.");
                continue;
            }

            var hasRequiredValues = !string.IsNullOrWhiteSpace(texts.Subject)
                && !string.IsNullOrWhiteSpace(texts.Title)
                && !string.IsNullOrWhiteSpace(texts.Body)
                && !string.IsNullOrWhiteSpace(texts.LifetimeLabel);
            if (!hasRequiredValues)
            {
                failures.Add(
                    $"{nameof(OtpMailOptions.Texts)}[{language}] needs a {nameof(OtpMailTexts.Subject)}, a {nameof(OtpMailTexts.Title)}, "
                    + $"a {nameof(OtpMailTexts.Body)} and a {nameof(OtpMailTexts.LifetimeLabel)}.");
            }
        }
    }

    private static void ValidateDefaultLanguage(OtpMailOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultLanguage))
        {
            failures.Add($"{nameof(OtpMailOptions.DefaultLanguage)} is required.");
            return;
        }

        if (!options.Texts.ContainsKey(options.DefaultLanguage))
        {
            failures.Add($"{nameof(OtpMailOptions.DefaultLanguage)} '{options.DefaultLanguage}' has no {nameof(OtpMailOptions.Texts)}.");
        }
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColorPattern();
}
