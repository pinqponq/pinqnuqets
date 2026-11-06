# Pinqponq.Identity.Otp.Mail

A branded HTML email for the one-time codes that `Pinqponq.Identity.Otp` sends. The product
gives its name, logo, colors and texts; the package picks the recipient's language, fills in
the code and its lifetime, renders the HTML body with a plain-text version and hands the mail
to `IOtpService`, which sends it through `Pinqponq.Mail`.

The product keeps what only it knows: its brand values, every text that is not English, and
how to find a recipient's language.

## Install

```bash
dotnet add package Pinqponq.Identity.Otp.Mail
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- `AddPinqponqOtp` from `Pinqponq.Identity.Otp`: it generates the code and its `OtpOptions.Ttl`
  is the lifetime the mail shows
- `AddPinqponqMail` from `Pinqponq.Mail`: it sends the mail

## Quick start

```csharp
using Pinqponq.Identity.Otp.DependencyInjection;
using Pinqponq.Identity.Otp.Mail;
using Pinqponq.Identity.Otp.Mail.DependencyInjection;
using Pinqponq.Mail.DependencyInjection;

builder.Services.AddPinqponqOtp(options => builder.Configuration.GetSection("Otp").Bind(options));
builder.Services.AddPinqponqMail(mail => builder.Configuration.GetSection("Smtp").Bind(mail));
builder.Services.AddPinqponqOtpMail(options =>
{
    options.ProductName = "Acme";
    options.LogoUrl = "https://cdn.acme.example/logo.png";   // optional, the header is hidden when empty
    options.CallToActionUrl = "https://app.acme.example/";   // optional, the button is hidden when empty
    options.Colors.Accent = "#0b57d0";                        // every color has a neutral default
    options.Texts["de"] = new OtpMailTexts
    {
        Subject = "Ihr {ProductName}-Bestätigungscode",
        Title = "Ihr Bestätigungscode",
        Body = "Geben Sie diesen Code in {ProductName} ein.",
        LifetimeLabel = "{Minutes} Minuten gültig",
        LifetimeLabelOneMinute = "1 Minute gültig",
    };
    options.DefaultLanguage = "en";
});
```

`IOtpService.GenerateAndSendAsync("user@example.com")` now sends this mail. SMS codes are not
changed. The order of the three calls does not matter.

The options can also be bound from configuration, texts and colors included:

```csharp
builder.Services.AddPinqponqOtpMail(options => builder.Configuration.GetSection("OtpMail").Bind(options));
```

## Configuration

| Option | Default | Description |
|---|---|---|
| `ProductName` | none, required | Replaces `{ProductName}` in the texts. It is also the logo's alternative text and the footer when no `Footer` text is set. |
| `LogoUrl` | none | Absolute http(s) URL of the logo in the header. Without it there is no header. |
| `CallToActionUrl` | none | Absolute http(s) URL of the button. The button is shown only when this and the language's `CallToActionLabel` are both set. |
| `Colors` | neutral grays and blue | `PageBackground`, `CardBackground`, `HeaderBackground`, `Accent` (code and button), `OnAccent` (button label), `CodeBackground`, `Title`, `Text`, `Muted` (lifetime label and footer). Each is `#rgb` or `#rrggbb`. |
| `DefaultLanguage` | `en` | Used when the recipient has no language or one without texts. It must have texts. |
| `Texts` | English | Texts per language; the key is matched without regard to case. |

`OtpMailTexts` of one language:

| Text | Required | English default |
|---|---|---|
| `Subject` | yes | `Your {ProductName} verification code` |
| `Preheader` | no | `Your verification code is {Code}.` (the preview mail clients show next to the subject) |
| `Title` | yes | `Your verification code` |
| `Greeting` | no | `Hello,` |
| `Body` | yes | `Enter this code in {ProductName} to continue. If you did not ask for it, you can ignore this email.` |
| `LifetimeLabel` | yes | `Valid for {Minutes} minutes` |
| `LifetimeLabelOneMinute` | no | `Valid for 1 minute` (used instead of `LifetimeLabel` when the lifetime is one minute) |
| `CallToActionLabel` | no | `Open {ProductName}` |
| `Footer` | no | `This email was sent automatically by {ProductName}.` |

Every text may contain `{ProductName}`, `{Code}` and `{Minutes}`. `{Minutes}` is
`OtpOptions.Ttl` in whole minutes, rounded up and at least 1, so the mail always shows the
lifetime the code really has. An optional text that is empty is left out.

The options are validated at startup: a missing `ProductName`, a URL that is not absolute
http(s), a color that is not a hex color, a language without one of its required texts, and a
`DefaultLanguage` without texts stop the host.

## Language

The package asks `IOtpMailLanguageResolver` for the recipient's language. The default returns
none, so every mail uses `DefaultLanguage`. Register your own to read the user's preference:

```csharp
public sealed class UserLanguageResolver(AppDbContext db) : IOtpMailLanguageResolver
{
    public async Task<string?> ResolveLanguageAsync(string recipient, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(user => user.Email == recipient, cancellationToken);
        return user?.Language;
    }
}

builder.Services.AddScoped<IOtpMailLanguageResolver, UserLanguageResolver>();
```

An exception from the resolver stops the send, and `IOtpService` removes the stored code. A
resolver that should fall back to the default language on a failure catches and logs the
failure itself and returns `null`.

## Layout

`OtpMailTemplate` is the default layout: an optional logo header, the title, a panel with the
lifetime label and the code, the greeting and body, an optional button and the footer. Every
value is HTML-encoded.

For a completely different layout, register your own `IOtpMailTemplate`. It gets an
`OtpMailModel` with the chosen language, the code, the minutes, the brand values and the texts
with their placeholders already filled in. These values are not HTML-encoded, so the template
must encode them (for example with `System.Net.WebUtility.HtmlEncode`).

```csharp
builder.Services.AddSingleton<IOtpMailTemplate, MyOtpMailTemplate>();
```

The subject and the plain-text version are built by the package from the same texts, also when
a custom template is used.

## How it plugs into Pinqponq.Identity.Otp

`IOtpService` builds every code email through `IOtpMessageComposer`. `AddPinqponqOtpMail`
replaces the default composer, which fills `OtpOptions.EmailSubjectTemplate` and
`EmailBodyTemplate`, with `OtpMailMessageComposer`. Those two templates are not used while this
package is registered.

## Related packages

- [Pinqponq.Identity.Otp](../Pinqponq.Identity.Otp/README.md) — generates, sends and verifies the codes.
- [Pinqponq.Mail](../Pinqponq.Mail/README.md) — SMTP mail sending (`IEmailSender`).
