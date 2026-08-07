# Pinqponq.Identity.Otp

One-time code send/verify flow over email or SMS. Channel routing (mail/sms)
lives inside this package; the storage interface for pending codes is left to
the consuming application, matching the interface-only approach used by
`Pinqponq.Identity`'s refresh tokens.

## Install

```bash
dotnet add package Pinqponq.Identity.Otp
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- An `IOtpStore` implementation from your application (Redis, EF Core, …) —
  required
- [`Pinqponq.Sms`](../Pinqponq.Sms/README.md) (`AddPinqponqSms`) if you send
  codes over SMS
- [`Pinqponq.Mail`](../Pinqponq.Mail/README.md) (`AddPinqponqMail`) if you
  send codes over email
- A `HashPepper` secret of at least 32 characters

## Quick start

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Identity.Otp;
using Pinqponq.Identity.Otp.DependencyInjection;
using Pinqponq.Mail.DependencyInjection; // AddPinqponqMail

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqOtp(options =>
{
    options.CodeLength = 6;
    options.Ttl = TimeSpan.FromMinutes(3);
    options.MaxAttempts = 5;
    options.MinSendInterval = TimeSpan.FromSeconds(30);
    options.HashPepper = builder.Configuration["Otp:HashPepper"]!; // >= 32 chars
    options.EmailSubjectTemplate = "Your {0}-digit verification code";
    options.EmailBodyTemplate = "Your verification code is {0}. It expires in 3 minutes.";
});

// A sender is only required for the channel this application actually uses.
builder.Services.AddPinqponqMail(mail => builder.Configuration.GetSection("Smtp").Bind(mail));

// Application-owned persistence for pending OTP records (see "Main types" below).
builder.Services.AddScoped<IOtpStore, MyOtpStore>();

var app = builder.Build();

app.MapPost("/otp/send", async (
    SendOtpRequest request,
    IOtpService otpService,
    CancellationToken cancellationToken) =>
{
    // channel defaults to OtpChannel.Auto — email if the recipient contains '@', SMS otherwise.
    await otpService.GenerateAndSendAsync(
        recipient: request.Email,
        channel: OtpChannel.Email,
        purpose: "login",
        cancellationToken: cancellationToken);

    return Results.Accepted();
});

app.MapPost("/otp/verify", async (
    VerifyOtpRequest request,
    IOtpService otpService,
    CancellationToken cancellationToken) =>
{
    var status = await otpService.VerifyAsync(
        recipient: request.Email,
        code: request.Code,
        purpose: "login",
        cancellationToken: cancellationToken);

    return status switch
    {
        OtpVerifyStatus.Success => Results.Ok(),
        OtpVerifyStatus.Expired => Results.BadRequest("Code expired, request a new one."),
        OtpVerifyStatus.TooManyAttempts => Results.BadRequest("Too many attempts."),
        _ => Results.BadRequest("Invalid code."),
    };
});

app.Run();
```

## Configuration

`OtpOptions` (configured via `AddPinqponqOtp`, validated on startup). Message
templates use `{0}` as the code placeholder.

| Option | Default | Notes |
|---|---|---|
| `CodeLength` | 6 | Must be between 4 and 12. |
| `Ttl` | 180 seconds | Must be positive. Code lifetime. |
| `MaxAttempts` | 5 | Must be greater than zero. |
| `MinSendInterval` | 30 seconds | Passed to `IOtpSendRateLimiter`. Must be `>= 0`. The default limiter is a no-op — see "Notes / behavior". |
| `HashPepper` | — | Required. Must be **at least 32 characters**. Mixed into the code hash via HMAC-SHA256. |
| `SmsTemplate` | `"Your verification code: {0}"` | SMS body. |
| `EmailSubjectTemplate` | `"Your verification code: {0}"` | Email subject. |
| `EmailBodyTemplate` | `"Your verification code: {0}"` | Email body. |

## Main types

| Type | Lifetime | Description |
|---|---|---|
| `IOtpService` | Scoped | `GenerateAndSendAsync(recipient, channel, purpose, ct)`, `VerifyAsync(recipient, code, purpose, ct)`. Requires the application to register `IOtpStore`. |
| `IOtpStore` | App-provided | `SaveAsync`, `FindAsync`, `UpdateAsync`, `RemoveAsync`, `TryRemoveAsync`, `TryConsumeAsync`. |
| `IOtpSendRateLimiter` | Singleton (`AllowAllOtpSendRateLimiter` by default — no-op) | `TryAcquireAsync(key, minInterval, ct)`. Replace with a real limiter (e.g. Redis-backed) to enforce `MinSendInterval`. |
| `OtpChannel` | Enum | `Auto` (email if recipient contains `@`, else SMS), `Sms`, `Email`. |
| `OtpVerifyStatus` | Enum | `Success`, `NotFound`, `Expired`, `TooManyAttempts`, `Mismatch`. |
| `OtpRecord` | Model | `Key`, `CodeHash`, `Recipient`, `ExpiresAt`, `Attempts`, `CreatedAt`. The raw code is never persisted. |
| `OtpSendRateLimitedException` | Exception | Thrown by `GenerateAndSendAsync` when `IOtpSendRateLimiter.TryAcquireAsync` returns `false`. |

`IOtpStore.TryConsumeAsync` is the core contract implementations must get
right — it performs expiry check, attempt-limit check, hash comparison,
remove-on-success and attempts-increment-on-mismatch **atomically**:

```csharp
public sealed class MyOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, OtpRecord> _records = new();
    private readonly object _gate = new();

    public Task SaveAsync(OtpRecord record, CancellationToken ct = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> FindAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_records.GetValueOrDefault(key));

    public Task UpdateAsync(OtpRecord record, CancellationToken ct = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> TryRemoveAsync(string key, string expectedCodeHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_records.TryGetValue(key, out var record) && record.CodeHash == expectedCodeHash)
            {
                return Task.FromResult(_records.TryRemove(key, out _));
            }

            return Task.FromResult(false);
        }
    }

    public Task<OtpVerifyStatus> TryConsumeAsync(
        string key, string codeHash, int maxAttempts, DateTimeOffset utcNow, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return Task.FromResult(OtpVerifyStatus.NotFound);
            }

            if (utcNow >= record.ExpiresAt)
            {
                _records.TryRemove(key, out _);
                return Task.FromResult(OtpVerifyStatus.Expired);
            }

            if (record.Attempts >= maxAttempts)
            {
                return Task.FromResult(OtpVerifyStatus.TooManyAttempts);
            }

            if (record.CodeHash != codeHash)
            {
                record.Attempts++;
                return Task.FromResult(OtpVerifyStatus.Mismatch);
            }

            _records.TryRemove(key, out _);
            return Task.FromResult(OtpVerifyStatus.Success);
        }
    }
}
```

## Notes / behavior

- **Only the code's hash is ever persisted** (`OtpRecord.CodeHash`, via
  HMAC-SHA256 keyed with `HashPepper`) — the raw code exists only in memory
  long enough to be sent.
- **Channel routing lives in this package.** `OtpChannel.Auto` picks email
  when the recipient contains `@`, otherwise SMS. A sender is resolved lazily
  when a code is actually sent, so an application that only sends OTPs by
  email does not need to call `AddPinqponqSms` — the sender is registered via
  `services.TryAddScoped<IOtpService>(...)` with `ISmsSender?`/`IEmailSender?`
  as optional dependencies; whichever one is missing only throws when a send
  is routed to that channel, naming the registration to add
  (`AddPinqponqSms` / `AddPinqponqMail`).
- **`IOtpStore` is required at first resolve, not at registration.**
  `IOtpService` is registered through a factory so ASP.NET Core's container
  validation in Development does not abort startup for an app that has not
  yet wired a store; the missing `IOtpStore` is reported as an
  `InvalidOperationException` the first time `IOtpService` is resolved.
- **Rate limiting is opt-in.** The default `IOtpSendRateLimiter` is a no-op
  that always allows the send — register your own (Redis, in-memory, …) to
  actually enforce `MinSendInterval` and throw `OtpSendRateLimitedException`
  on abuse.
- **Delivery failure rolls back the code.** If `GenerateAndSendAsync` fails to
  deliver, the record is removed via the compare-and-set `TryRemoveAsync`
  (matching on `CodeHash`) so a concurrent, newer `GenerateAndSendAsync` call
  for the same recipient/purpose is never accidentally deleted.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Sms](../Pinqponq.Sms/README.md) — SMS sending (`ISmsSender`),
  needed when routing codes to the SMS channel.
- [Pinqponq.Mail](../Pinqponq.Mail/README.md) — SMTP mail sending
  (`IEmailSender`), needed when routing codes to the email channel.
- [Pinqponq.Identity](../Pinqponq.Identity/README.md) — JWT/refresh-token/
  password primitives, typically issued right after a successful OTP
  verification.
- [Pinqponq.Auth.Totp](../Pinqponq.Auth.Totp/README.md) — an alternative
  2FA factor (RFC 6238 TOTP) that does not require sending anything.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
