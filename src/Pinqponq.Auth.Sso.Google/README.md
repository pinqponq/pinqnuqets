# Pinqponq.Auth.Sso.Google

Sign-in implementation via Google OAuth2/OIDC `id_token` validation. Implements
the shared [`IExternalAuthProvider`](../Pinqponq.Auth.Sso.Abstractions/README.md)
contract using [`Google.Apis.Auth`](https://www.nuget.org/packages/Google.Apis.Auth)
(`GoogleJsonWebSignature`), so no manual JWKS fetching or signature checking
is required.

## Install

```bash
dotnet add package Pinqponq.Auth.Sso.Google
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A Google OAuth 2.0 client ID (from the Google Cloud Console) — the audience
  the client-side sign-in flow issues `id_token`s for
- The client obtains the `id_token` itself (e.g. Google Identity Services /
  Sign in with Google on the frontend); this package only **validates** an
  already-issued `id_token` — it does not perform the browser/redirect flow
  or exchange an authorization code

## Quick start

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Auth.Sso.Abstractions;
using Pinqponq.Auth.Sso.Google.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqGoogleSso(options =>
{
    options.ClientIds.Add(builder.Configuration["Google:ClientId"]!);
    options.RequireEmailVerified = true; // default
    // options.HostedDomain = "example.com"; // restrict to a Google Workspace domain
    // options.RequireNonce = true;          // enable for browser OIDC flows that send a nonce
});

var app = builder.Build();

app.MapPost("/auth/google", async (
    GoogleSignInRequest request,
    IExternalAuthProvider googleProvider, // resolved by DI; ProviderName == "Google"
    CancellationToken cancellationToken) =>
{
    var result = await googleProvider.AuthenticateAsync(
        ExternalAuthRequest.FromIdToken(request.IdToken, nonce: request.Nonce),
        cancellationToken);

    if (!result.Succeeded)
    {
        return Results.Unauthorized();
    }

    var user = result.User!;

    // Evaluate hd/email verification before auto-linking by email — see
    // "Notes / behavior" below. Look up or create a local account keyed by
    // (Provider, Subject), then issue your own session/JWT, e.g. via
    // Pinqponq.Identity's IJwtTokenGenerator.
    var account = await MyAccountLookup.FindOrCreateAsync(
        provider: user.Provider,
        subject: user.Subject,
        email: user.Email,
        cancellationToken: cancellationToken);

    return Results.Ok(new { account.Id, user.Name, user.Picture });
});

app.Run();
```

If you have multiple `IExternalAuthProvider` implementations registered
(Google plus others), resolve the specific one either by injecting
`IEnumerable<IExternalAuthProvider>` and filtering on `ProviderName ==
GoogleAuthProvider.Name` (`"Google"`), or by keeping a single
`IExternalAuthProvider` registration per provider and routing by an
endpoint/route segment as shown above.

## Configuration

`GoogleAuthOptions` (configured via `AddPinqponqGoogleSso`, validated on
startup):

| Option | Default | Notes |
|---|---|---|
| `ClientIds` | empty | Required — must contain at least one non-empty entry. The accepted audience(s); the `id_token` must have been issued for one of these Google OAuth client IDs. |
| `HostedDomain` | `null` | Optional Google Workspace hosted domain (`hd` claim) the token must match. When empty, hosted-domain validation is skipped. |
| `RequireEmailVerified` | `true` | When `true`, authentication fails unless Google's payload reports `email_verified`. |
| `RequireNonce` | `false` | When `true`, the request's `Nonce` must be present and match the `id_token`'s `nonce` claim (replay binding). Defaults to `false` because native/mobile `id_token` flows may not use a nonce; enable it for browser-based OIDC flows. |

## Main types

| Type | Description |
|---|---|
| `GoogleAuthProvider` | The `IExternalAuthProvider` implementation. `ProviderName` returns the constant `GoogleAuthProvider.Name` (`"Google"`). Registered as a singleton `IExternalAuthProvider` by `AddPinqponqGoogleSso`. |
| `GoogleAuthOptions` | Configuration bound at startup (see above). |

`GoogleAuthProvider.AuthenticateAsync` only supports the **id-token flow**:
calling it with `ExternalAuthRequest.FromAuthorizationCode(...)` (and no
`IdToken`) returns a failure result rather than performing a code exchange.

## Notes / behavior

- **Only the id-token flow is supported.** Your frontend must obtain the
  `id_token` from Google (e.g. Google Identity Services) and send it to your
  backend; this package validates that token's signature, audience, issuer,
  and expiry via `GoogleJsonWebSignature.ValidateAsync` — it does not handle
  authorization-code exchange.
- **⚠️ Evaluate `hd` and email verification before auto-linking an account by
  email.** `ExternalUserInfo.Email`/`EmailVerified` come straight from
  Google's payload. Before treating a Google sign-in as "the same user" as
  an existing local account with a matching email, confirm
  `EmailVerified` is `true` (enforced by `RequireEmailVerified`, on by
  default) and, if you restrict sign-in to a Workspace domain, that `hd`
  matches your expected domain (`HostedDomain`). A personal `@gmail.com`
  address with an unverified or attacker-controlled email on another
  provider should never be auto-linked to an internal account purely by
  string-matching the email address — prefer keying local accounts by
  `(Provider, Subject)` and treating `Email` as a hint for account discovery
  at most.
- **Nonce checking is opt-in.** When `RequireNonce` is `true`, a request
  without a `Nonce` fails immediately; when a `Nonce` is supplied, it is
  compared to the token's `nonce` claim using a fixed-time comparison.
- **Validation failures are intentionally generic.** Invalid/expired/
  malformed tokens, audience mismatches, and nonce/email-verification
  failures all surface as `ExternalAuthResult.Failure("Invalid Google
  id_token.")` rather than leaking which specific check failed, to avoid
  giving an attacker a token-validation oracle.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Auth.Sso.Abstractions](../Pinqponq.Auth.Sso.Abstractions/README.md) —
  the `IExternalAuthProvider` contract this package implements.
- [Pinqponq.Identity](../Pinqponq.Identity/README.md) — issue your own
  JWT/refresh tokens for the local session once Google sign-in succeeds.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
