# Pinqponq.Mail

SMTP-based email sending wrapper with a standard `IEmailSender` interface, built
directly on `System.Net.Mail` — no third-party mail client dependency. Supports
comma/semicolon-separated `To`/`Cc`/`Bcc` lists and file attachments confined to a
configured root directory.

## Install

```bash
dotnet add package Pinqponq.Mail
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- An SMTP server (host, port, and optionally credentials)

## Quick start

Configure inline:

```csharp
using Pinqponq.Mail;
using Pinqponq.Mail.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqMail(options =>
{
    options.SmtpHost = "smtp.example.com";
    options.SmtpPort = 587;
    options.SmtpUsername = builder.Configuration["Smtp:Username"];
    options.SmtpPassword = builder.Configuration["Smtp:Password"];
    options.FromEmail = "no-reply@example.com";
    options.FromName = "My App";
});

var app = builder.Build();
```

...or bind from configuration (defaults to the `"Smtp"` section):

```csharp
builder.Services.AddPinqponqMail(builder.Configuration);
// or a custom section name:
builder.Services.AddPinqponqMail(builder.Configuration, sectionName: "Mail:Smtp");
```

```json
{
  "Smtp": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SmtpUsername": "apikey",
    "SmtpPassword": "secret",
    "FromEmail": "no-reply@example.com",
    "FromName": "My App",
    "EnableSsl": true
  }
}
```

Send a message from anywhere `IEmailSender` is injected:

```csharp
public sealed class WelcomeEmailService(IEmailSender emailSender)
{
    public Task SendAsync(string to, CancellationToken cancellationToken) =>
        emailSender.SendAsync(
            new EmailMessage
            {
                To = to,
                Subject = "Welcome!",
                Body = "<p>Thanks for signing up.</p>",
                IsBodyHtml = true,
            },
            cancellationToken);
}
```

## Configuration

Two overloads of `AddPinqponqMail` register `IEmailSender` (as `SmtpEmailSender`)
and validate `SmtpOptions` on startup via `ValidateOnStart()`:

- `AddPinqponqMail(Action<SmtpOptions> configure)` — inline configuration.
- `AddPinqponqMail(IConfiguration configuration, string sectionName = "Smtp")` —
  binds from a configuration section. Throws `InvalidOperationException` at
  registration time if the section doesn't exist.

| Option | Default | Notes |
|---|---|---|
| `SmtpHost` | `""` | Required. |
| `SmtpPort` | `0` | Required; must be between 1 and 65535. |
| `SmtpUsername` | `""` | Optional. When set, `SmtpClient.Credentials` is configured with it (and `SmtpPassword`); when blank, the connection is anonymous. |
| `SmtpPassword` | `""` | Paired with `SmtpUsername`. |
| `FromEmail` | `""` | Required. Used as the `From` address on every outgoing message. |
| `FromName` | `null` | Optional `From` display name. |
| `EnableSsl` | `true` | Whether `SmtpClient` uses SSL/TLS. |
| `AttachmentRoot` | `null` | Root directory that attachment paths must resolve under. **Required** when `EmailMessage.Attachments` is non-empty — see [Notes / behavior](#notes--behavior). |

`SmtpOptionsValidator` (an internal `IValidateOptions<SmtpOptions>`) enforces
`SmtpHost`, `SmtpPort`, and `FromEmail` at startup; a missing `AttachmentRoot` is
only surfaced later, when a message with attachments is actually sent.

## Main types

- **`AddPinqponqMail`** — `IServiceCollection` extensions (inline or
  `IConfiguration`-bound) that register `IEmailSender` and its options.
- **`IEmailSender`** — the sending contract: `Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)`.
- **`EmailMessage`** — `To` and `Subject`/`Body` are `required`; `Cc`, `Bcc` are
  optional comma/semicolon-separated address lists; `IsBodyHtml` defaults to `true`;
  `Attachments` is an optional list of file paths.
- **`SmtpOptions`** — configuration as described above.
- **`SmtpEmailSender`** — the `IEmailSender` implementation, built on
  `System.Net.Mail.SmtpClient` and `MailMessage`.

## Notes / behavior

- **Attachments require `AttachmentRoot` — this is a path jail, not just a config
  toggle.** If `EmailMessage.Attachments` is non-empty but `SmtpOptions.AttachmentRoot`
  is blank, `SendAsync` throws `ArgumentException` immediately. When `AttachmentRoot`
  *is* set, every attachment path is resolved with `Path.GetFullPath` and must land
  inside that root directory (or equal it exactly); a path that resolves outside the
  root — including via `..` traversal — is rejected with `ArgumentException` rather
  than being opened. A path that doesn't exist on disk after resolution also throws
  `ArgumentException`. This exists to stop caller-supplied paths from being used to
  read arbitrary files off the host as "attachments."
- **`To`/`Cc`/`Bcc` accept lists.** Each is split on `,` and `;`, trimmed, and empty
  entries are dropped. `To` must resolve to at least one address or `SendAsync`
  throws `ArgumentException`.
- **Validation order**: recipient, subject, and body are checked before any SMTP
  connection is attempted; attachment path validation happens after that, still
  before the message is sent.
- No built-in retry: unlike `Pinqponq.Sms`, this package does not wrap sends in a
  Polly retry pipeline — `SmtpClient.SendMailAsync` failures propagate directly. Add
  your own retry policy around `IEmailSender.SendAsync` if you need one.
- Because it's built on `System.Net.Mail`, this package requires no external SMTP
  client library, but also inherits `SmtpClient`'s behavior and limitations (e.g.
  synchronous DNS/connection setup, no built-in connection pooling across calls).

## Related packages

- [`Pinqponq.Identity.Otp`](../Pinqponq.Identity.Otp) — sends one-time passwords over
  email (and SMS) using this package's `IEmailSender` as its email delivery channel.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
