# Pinqponq.Auth.Totp

RFC 6238 TOTP-based two-factor authentication: secret generation, `otpauth://`
provisioning URI (scannable as a QR code), and code validation. Compatible
with Google Authenticator and Microsoft Authenticator.

## Install

```bash
dotnet add package Pinqponq.Auth.Totp
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- An `ITotpReplayStore` implementation from your application (Redis, EF Core,
  …) if you use `ValidateAsync` for replay-protected validation — optional,
  but recommended

## Quick start

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Auth.Totp;
using Pinqponq.Auth.Totp.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqTotp(options =>
{
    options.Issuer = "Example App";
    options.Digits = 6;
    options.PeriodSeconds = 30;
    options.ValidationWindow = 1; // tolerate ±30s clock drift
});

// Recommended: replay protection for ValidateAsync.
builder.Services.AddScoped<ITotpReplayStore, MyTotpReplayStore>();

var app = builder.Build();

// Enrollment: generate a secret and hand the user a QR code / manual entry key.
app.MapPost("/2fa/totp/enroll", (ITotpService totp, string userEmail) =>
{
    var secret = totp.GenerateSecret();
    var provisioningUri = totp.GetProvisioningUri(secret, accountName: userEmail);

    // Persist `secret` against the user record (encrypted at rest) before returning,
    // and surface `provisioningUri` as a QR code for the authenticator app to scan.
    return Results.Ok(new { secret, provisioningUri });
});

// Verification: prefer ValidateAsync so the same code cannot be replayed.
app.MapPost("/2fa/totp/verify", async (
    VerifyTotpRequest request,
    ITotpService totp,
    CancellationToken cancellationToken) =>
{
    var secret = await MyUserLookup.GetTotpSecretAsync(request.UserId, cancellationToken);

    var isValid = await totp.ValidateAsync(
        secret,
        request.Code,
        subjectKey: request.UserId,
        cancellationToken: cancellationToken);

    return isValid ? Results.Ok() : Results.BadRequest("Invalid or already-used code.");
});

app.Run();
```

## Configuration

`TotpOptions` (configured via `AddPinqponqTotp`, validated on startup):

| Option | Default | Notes |
|---|---|---|
| `Digits` | 6 | Must be between 6 and 8. |
| `PeriodSeconds` | 30 | Must be greater than zero. Time-step length. |
| `Algorithm` | `TotpAlgorithm.Sha1` | `Sha1` (RFC 6238 / authenticator-app default), `Sha256`, or `Sha512`. |
| `ValidationWindow` | 1 | Must be between 0 and 10. Number of time steps checked on each side of the current step (±30s at the default period). |
| `SecretByteLength` | 20 | Must be at least 16. Bytes of entropy in a generated secret. |
| `Issuer` | `"Pinqponq"` | Required (non-empty). Embedded in the provisioning URI and shown in authenticator apps. |

## Main types

| Type | Lifetime | Description |
|---|---|---|
| `ITotpService` | Scoped | `GenerateSecret()`, `GetProvisioningUri(secret, accountName, issuer?)`, `ComputeCode(secret, timestamp?)`, `Validate(secret, code, timestamp?)`, `ValidateAsync(secret, code, subjectKey, timestamp?, ct)`. |
| `ITotpReplayStore` | App-provided (optional, used by `ValidateAsync`) | `TryAcceptAsync(subjectKey, counter, ct)` — atomically accepts a matched time-step counter; returns `false` if it was already accepted. |
| `TotpAlgorithm` | Enum | `Sha1`, `Sha256`, `Sha512`. |

## Notes / behavior

- **Prefer `ValidateAsync` over the synchronous `Validate`.** `Validate` is a
  pure crypto check against the current window — it does **not** consult
  `ITotpReplayStore`, so a leaked or observed code stays valid until it
  expires and can be reused within the window. `ValidateAsync` records the
  matched time-step counter via `ITotpReplayStore.TryAcceptAsync`, so the
  same code cannot be accepted twice for the same `subjectKey`.
- **`ITotpReplayStore` is optional at registration, required at call time.**
  `ITotpService` is registered through a factory (scoped) so an application
  that only ever uses the synchronous `Validate`/`ComputeCode` never needs to
  register a store; calling `ValidateAsync` without one throws an
  `InvalidOperationException` naming `AddScoped<ITotpReplayStore, ...>()` as
  the fix.
- **`GetProvisioningUri` builds a standard `otpauth://totp/...` URI** —
  render it as a QR code (e.g. with a client-side or server-side QR library;
  this package does not generate images) for Google Authenticator, Microsoft
  Authenticator, or any RFC 6238-compatible app to scan.
- **Clock drift tolerance** is controlled by `ValidationWindow` — each unit
  checks one additional time step (`PeriodSeconds`) on both sides of "now".
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Identity](../Pinqponq.Identity/README.md) — JWT/refresh-token
  issuance, typically performed right after a successful TOTP check.
- [Pinqponq.Identity.Otp](../Pinqponq.Identity.Otp/README.md) — an
  alternative 2FA factor delivered over email/SMS instead of an authenticator
  app.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
