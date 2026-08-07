# Pinqponq.Auth.Sso.Abstractions

Shared contract for external identity providers (Google, and future
providers): `IExternalAuthProvider` and its supporting models. This is a
**contracts-only** package — no dependency injection, no implementation, no
`AddPinqponqXxx(...)` extension. It exists so provider packages (e.g.
[Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md)) and the
applications that consume them can share one normalized surface instead of
each provider inventing its own shapes.

## Install

```bash
dotnet add package Pinqponq.Auth.Sso.Abstractions
```

In practice you rarely install this package directly — a provider package
such as `Pinqponq.Auth.Sso.Google` already depends on it and brings it in
transitively. Install it directly only if you are writing your own provider
implementation or need the contract types in a project that doesn't
reference a concrete provider (e.g. a shared interfaces/contracts project).

## Requirements

- .NET 8.0, 9.0, or 10.0
- No third-party dependencies — this package has zero `PackageReference`
  entries

## Quick start

This package has no services to register; it only ships types. A typical
consumer depends on it indirectly through a provider package and codes
against the interface, optionally resolving a specific provider by name when
more than one is registered:

```csharp
using Pinqponq.Auth.Sso.Abstractions;

public sealed class ExternalLoginEndpoint(IEnumerable<IExternalAuthProvider> providers)
{
    public async Task<IResult> HandleAsync(
        string providerName,
        string idToken,
        CancellationToken cancellationToken)
    {
        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Results.BadRequest($"Unknown provider '{providerName}'.");
        }

        var request = ExternalAuthRequest.FromIdToken(idToken);
        var result = await provider.AuthenticateAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        var user = result.User!;
        // Look up or create a local account keyed by (Provider, Subject) — see
        // "Notes / behavior" below for why Email alone is not a safe join key.
        return Results.Ok(new { user.Subject, user.Provider, user.Email, user.Name });
    }
}
```

Implementing your own provider (the pattern followed by
`Pinqponq.Auth.Sso.Google`):

```csharp
using Pinqponq.Auth.Sso.Abstractions;

public sealed class MyProviderAuthProvider : IExternalAuthProvider
{
    public string ProviderName => "MyProvider";

    public async Task<ExternalAuthResult> AuthenticateAsync(
        ExternalAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return ExternalAuthResult.Failure("An id_token is required.");
        }

        // Validate the token against the provider's public keys / introspection endpoint...
        var payload = await ValidateTokenAsync(request.IdToken, cancellationToken);

        return ExternalAuthResult.Success(new ExternalUserInfo
        {
            Subject = payload.Subject,
            Provider = ProviderName,
            Email = payload.Email,
            EmailVerified = payload.EmailVerified,
            Name = payload.Name,
        });
    }
}
```

## Configuration

There is nothing to configure here and no `AddPinqponqXxx(...)` extension in
this package — it is a pure contract with no options and no DI registration.
Configuration (client IDs, hosted domain, nonce requirements, etc.) lives in
the concrete provider package. See
[Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md#configuration)
for a working example.

## Main types

| Type | Description |
|---|---|
| `IExternalAuthProvider` | The contract every provider implements: `ProviderName` and `AuthenticateAsync(ExternalAuthRequest, ct)`. |
| `ExternalAuthRequest` | Input to an authentication attempt. Supports the id-token flow (`FromIdToken(idToken, nonce?)`) and the authorization-code flow (`FromAuthorizationCode(code, redirectUri)`); a provider uses whichever field(s) apply and may not support both flows. |
| `ExternalAuthResult` | Outcome of an attempt: `Succeeded`, `User` (populated on success via `Success(user)`), `Error` (populated on failure via `Failure(error)`). |
| `ExternalUserInfo` | Normalized external profile: `Subject`, `Provider`, `Email`, `EmailVerified`, `Name`, `GivenName`, `FamilyName`, `Picture`. |

## Notes / behavior

- **This package has no DI extension.** There is no `AddPinqponqXxx(...)`
  method to call — it only defines types. Register a concrete
  `IExternalAuthProvider` implementation from a provider package instead,
  e.g. `services.AddPinqponqGoogleSso(...)`.
- **`ExternalAuthRequest` covers two flows, but a given provider may support
  only one.** For example, `Pinqponq.Auth.Sso.Google`'s current
  implementation only supports the id-token flow and returns a failure
  result if `AuthorizationCode` is supplied instead.
- **Do not trust `Email`/`EmailVerified` for account linking without
  checking `EmailVerified`.** `ExternalUserInfo.Subject` (the provider's
  stable user id, e.g. Google's `sub`) is the only safe key for identifying
  "the same external account across logins". Joining local accounts purely
  by `Email` risks account takeover if a provider allows unverified emails
  or a different provider reuses the same address.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md) — the
  first concrete `IExternalAuthProvider` implementation (Google OAuth2/OIDC
  id_token validation).
- [Pinqponq.Identity](../Pinqponq.Identity/README.md) — issue your own
  JWT/refresh tokens once an external sign-in succeeds.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
