using System.Net;

namespace Pinqponq.Identity.Otp.Mail;

/// <summary>
/// Default <see cref="IOtpMailTemplate"/>: a single-column layout with an optional logo header,
/// the title, the code with its lifetime, the text, an optional button and the footer. Every
/// value is HTML-encoded.
/// </summary>
public sealed class OtpMailTemplate : IOtpMailTemplate
{
    public string Render(OtpMailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var colors = model.Colors;
        var headerHtml = BuildHeaderHtml(model);
        var greetingHtml = BuildParagraphHtml(model.Greeting, colors);
        var bodyHtml = BuildParagraphHtml(model.Body, colors);
        var callToActionHtml = BuildCallToActionHtml(model);

        return $$"""
            <!DOCTYPE html>
            <html lang="{{Encode(model.Language)}}">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{{Encode(model.Subject)}}</title>
            </head>
            <body style="margin: 0; padding: 24px 12px; background-color: {{Encode(colors.PageBackground)}}; font-family: Helvetica, Arial, sans-serif;">
                <div style="display: none; max-height: 0; overflow: hidden; opacity: 0;">{{Encode(model.Preheader ?? string.Empty)}}</div>
                <div style="max-width: 600px; margin: 0 auto; background-color: {{Encode(colors.CardBackground)}}; border-radius: 8px; overflow: hidden;">
                    {{headerHtml}}
                    <div style="padding: 32px 32px 16px;">
                        <h1 style="margin: 0 0 20px; font-size: 22px; text-align: center; color: {{Encode(colors.Title)}};">{{Encode(model.Title)}}</h1>
                        <div style="margin: 0 0 24px; padding: 20px; border-radius: 8px; text-align: center; background-color: {{Encode(colors.CodeBackground)}};">
                            <div style="margin-bottom: 8px; font-size: 12px; letter-spacing: 1px; text-transform: uppercase; color: {{Encode(colors.Muted)}};">{{Encode(model.LifetimeLabel)}}</div>
                            <div style="font-size: 36px; font-weight: bold; letter-spacing: 6px; line-height: 1; color: {{Encode(colors.Accent)}};">{{Encode(model.Code)}}</div>
                        </div>
                        {{greetingHtml}}
                        {{bodyHtml}}
                        {{callToActionHtml}}
                    </div>
                    <div style="padding: 16px 32px 24px; font-size: 12px; text-align: center; color: {{Encode(colors.Muted)}};">{{Encode(model.Footer)}}</div>
                </div>
            </body>
            </html>
            """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string BuildHeaderHtml(OtpMailModel model)
    {
        if (string.IsNullOrWhiteSpace(model.LogoUrl))
        {
            return string.Empty;
        }

        return $"""<div style="padding: 24px; text-align: center; background-color: {Encode(model.Colors.HeaderBackground)};"><img src="{Encode(model.LogoUrl)}" alt="{Encode(model.ProductName)}" height="48" style="display: block; margin: 0 auto; border: 0; height: 48px; width: auto;" /></div>""";
    }

    private static string BuildParagraphHtml(string? text, OtpMailColors colors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return $"""<p style="margin: 0 0 16px; font-size: 15px; line-height: 1.6; color: {Encode(colors.Text)};">{Encode(text)}</p>""";
    }

    private static string BuildCallToActionHtml(OtpMailModel model)
    {
        if (model.CallToActionUrl is null || model.CallToActionLabel is null)
        {
            return string.Empty;
        }

        return $"""<p style="margin: 24px 0 8px; text-align: center;"><a href="{Encode(model.CallToActionUrl)}" style="display: inline-block; background-color: {Encode(model.Colors.Accent)}; color: {Encode(model.Colors.OnAccent)}; font-size: 15px; font-weight: bold; text-decoration: none; padding: 12px 28px; border-radius: 6px;">{Encode(model.CallToActionLabel)}</a></p>""";
    }
}
