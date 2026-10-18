# Pinqponq.Billing.RevenueCat.Mail

Subscription event mails on top of `Pinqponq.Billing.RevenueCat` and `Pinqponq.Mail`. When
a webhook event is committed, the package picks the mail for the event, asks the product
where it goes, renders a branded HTML mail with a plain-text version and sends it. A mail
that cannot be sent is logged and never fails the webhook.

The product keeps what only it knows: how to find a subscriber's address and language, the
brand values, the copy in each language, which events send a mail and the links.

## Install

```bash
dotnet add package Pinqponq.Billing.RevenueCat.Mail
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- `AddPinqponqRevenueCat` from `Pinqponq.Billing.RevenueCat`: its handler calls the mail listener
- `AddPinqponqMail` from `Pinqponq.Mail`: it sends the mail

## Quick start

Tell the package where a subscriber's mail goes. The subscriber id is the one your
`IRevenueCatSubscriptionStore.TryResolveSubscriberId` returned:

```csharp
public sealed class UserMailRecipientResolver(AppDbContext db) : IRevenueCatMailRecipientResolver
{
    public async Task<RevenueCatMailRecipient?> Resolve(string subscriberId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(user => user.Id.ToString() == subscriberId, cancellationToken);
        return user is null ? null : new RevenueCatMailRecipient(user.Email, user.Language);
    }
}
```

Register it next to the webhook and mail packages:

```csharp
builder.Services.AddPinqponqMail(builder.Configuration, "Smtp");
builder.Services.AddPinqponqRevenueCat<SubscriptionStore>(options => { /* ... */ });
builder.Services.AddPinqponqRevenueCatMail<UserMailRecipientResolver>(options =>
{
    options.Brand.Name = "My App";
    options.Brand.LogoUrl = "https://example.com/logo.png";
    options.Brand.PrimaryColor = "#3366ff";
    options.Brand.FooterText = "© {BrandName}";

    options.Events[RevenueCatMailEvent.BillingIssue].Enabled = true;
    options.Events[RevenueCatMailEvent.BillingIssue].CallToActionUrl = "https://example.com/account";

    options.Texts["tr"] = new RevenueCatMailTexts
    {
        Events =
        {
            [RevenueCatMailEvent.InitialPurchase] = new RevenueCatMailText
            {
                Subject = "{BrandName} aboneliğin aktif",
                Title = "Abone olduğun için teşekkürler",
                Paragraphs = { "{BrandName} aboneliğin artık aktif." },
            },
        },
    };
});
```

## Configuration

`AddPinqponqRevenueCatMail<TResolver>(Action<RevenueCatMailOptions> configure)` registers
the mail listener (scoped, as an `IRevenueCatEventListener`), the default renderer
(singleton), your resolver (scoped) and validates the options on startup.

| Option | Default | Notes |
|---|---|---|
| `Brand.Name` | none | Replaces `{BrandName}` in every text. Required. |
| `Brand.LogoUrl` | none | Absolute http(s) URL. Without it the header shows the brand name. |
| `Brand.PrimaryColor` | `#333333` | Header and button color, `#rgb` or `#rrggbb`. |
| `Brand.OnPrimaryColor` | `#ffffff` | Color of the brand name and the button label on `PrimaryColor`, `#rgb` or `#rrggbb`. |
| `Brand.FooterText` | brand name | Footer line; `{BrandName}` is replaced. |
| `DefaultLanguage` | `en` | Used when the recipient has no language or one without texts. Required. |
| `RequirePremiumEntitlement` | `true` | Only events that carry `RevenueCatOptions.PremiumEntitlementId` send a mail. |
| `Events[event].Enabled` | `InitialPurchase` only | Which events send a mail. |
| `Events[event].CallToActionUrl` | none | Absolute http(s) link of the button. |
| `Events[event].TemplateOverride` | none | Full HTML template for the event, see below. |
| `Texts[language]` | English for every event | Subject, preheader, title, paragraphs and button label per event. |

Startup fails when the brand name is missing, the color or a URL is malformed, a text has no
subject or title, or an enabled event has no text in the default language.

## Events

| `RevenueCatMailEvent` | Webhook event |
|---|---|
| `InitialPurchase` | `INITIAL_PURCHASE` |
| `Renewal` | `RENEWAL` |
| `Cancellation` | `CANCELLATION` |
| `BillingIssue` | `BILLING_ISSUE` |
| `Expiration` | `EXPIRATION` |

Other event types never send a mail. Duplicate deliveries, invalid payloads and `TEST`
events never reach the listener (see `IRevenueCatEventListener`).

## Language

The recipient's language is tried first, then its base language (`pt` for `pt-BR`), then
`DefaultLanguage`; the first one with a text for the event is used. Language keys are
case-insensitive.

## Rendering

The default `RevenueCatMailRenderer` builds a single-column layout: a header in the primary
color with the logo (or the brand name), the title, the paragraphs, an optional button and
the footer, plus a plain-text version. The mail is sent as multipart/alternative through
`EmailMessage.PlainTextBody`. Every text and brand value is HTML-encoded.

To change the layout:

- set `Events[event].TemplateOverride` to your own HTML. It gets the placeholders in
  `RevenueCatMailTemplatePlaceholders` (`{Title}`, `{ParagraphsHtml}`, `{CallToActionHtml}`,
  `{BrandName}`, `{PrimaryColor}`, …), all HTML-encoded; or
- register your own `IRevenueCatMailRenderer` (before or after `AddPinqponqRevenueCatMail`).

## Logs

Each mail step is logged through `ILogger` and reported to `IRevenueCatWebhookObserver`, the
observer `Pinqponq.Billing.RevenueCat` already uses, with the names in
`RevenueCatMailLogEvents`:

| Event | Observer level | `ILogger` level | When |
|---|---|---|---|
| `webhook_revenuecat_mail_sent` | Info | Information | The mail was handed to `IEmailSender`. |
| `webhook_revenuecat_mail_skipped` | Info | Information | No mail: `reason` is one of `RevenueCatMailSkipReasons` (entitlement not premium, subscriber unresolved, resolver returned null, invalid address). |
| `webhook_revenuecat_mail_failed` | Warning | Error, with the exception | Resolving, rendering or sending threw. The webhook still succeeds. |

A disabled event logs nothing. The data carries the event id and type, the subscriber id and
the mail event, never the address. A cancellation of the webhook request is logged as failed
and then rethrown.
