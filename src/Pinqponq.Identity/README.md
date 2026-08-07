# Pinqponq.Identity

Generic auth primitives in one package: JWT issue/validate, refresh token
issue/rotate/revoke, and password hash/verify. The package owns the crypto and
lifecycle rules; storage for refresh tokens and revoked access tokens is left to
the consuming application so this package stays free of a database dependency.

## Install

```bash
dotnet add package Pinqponq.Identity
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- An `IRefreshTokenStore` implementation from your application if you use
  `IRefreshTokenService` (EF Core, Dapper, Redis, …)
- An `IAccessTokenRevocationStore` implementation from your application if you
  need to revoke access tokens by `jti` (e.g. logout before natural expiry) —
  optional
- A symmetric key of at least 32 bytes (256 bits) for HMAC signing, or a
  2048-bit+ RSA key pair for RSA signing

## Quick start

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Passwords;
using Pinqponq.Identity.RefreshTokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqIdentity(
    configureJwt: jwt =>
    {
        jwt.Issuer = "https://auth.example.com";
        jwt.Audience = "example-api";
        jwt.Algorithm = JwtSigningAlgorithm.HmacSha256;
        jwt.SymmetricKey = builder.Configuration["Jwt:SymmetricKey"]; // >= 32 bytes
        jwt.Lifetime = TimeSpan.FromMinutes(15);
    },
    configureRefreshTokens: refresh =>
    {
        refresh.Lifetime = TimeSpan.FromDays(14);
        refresh.ReuseDetectionGrace = TimeSpan.FromSeconds(5);
    });

// Application-owned persistence for refresh tokens (see "Main types" below).
builder.Services.AddScoped<IRefreshTokenStore, MyRefreshTokenStore>();

var app = builder.Build();

app.MapPost("/login", async (
    LoginRequest request,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtGenerator,
    IRefreshTokenService refreshTokenService,
    CancellationToken cancellationToken) =>
{
    var user = await MyUserLookup.FindByEmailAsync(request.Email, cancellationToken);
    var outcome = passwordHasher.Verify(user.PasswordHash, request.Password);
    if (outcome == PasswordVerificationOutcome.Failed)
    {
        return Results.Unauthorized();
    }

    if (outcome == PasswordVerificationOutcome.SuccessRehashNeeded)
    {
        user.PasswordHash = passwordHasher.Hash(request.Password);
        await MyUserLookup.SaveAsync(user, cancellationToken);
    }

    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) };
    var accessToken = jwtGenerator.GenerateToken(claims);
    var refreshToken = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

    return Results.Ok(new { accessToken, refreshToken = refreshToken.Token });
});

app.MapPost("/refresh", async (
    RefreshRequest request,
    IRefreshTokenService refreshTokenService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var rotated = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken);
        return Results.Ok(new { refreshToken = rotated.Token });
    }
    catch (InvalidRefreshTokenException)
    {
        return Results.Unauthorized();
    }
});

app.Run();
```

Registering a password hash for a new user, and validating an incoming access
token in a downstream handler:

```csharp
public sealed class AccountService(IPasswordHasher passwordHasher)
{
    public string HashNewPassword(string plaintext) => passwordHasher.Hash(plaintext);
}

public sealed class ProfileEndpoint(IJwtTokenValidator jwtValidator)
{
    public async Task<ClaimsPrincipal?> AuthenticateAsync(string bearerToken, CancellationToken ct) =>
        await jwtValidator.ValidateAsync(bearerToken, ct);
}
```

## Configuration

`JwtOptions` (configured via `configureJwt`, validated on startup):

| Option | Default | Notes |
|---|---|---|
| `Issuer` | — | Required. The `iss` claim and expected issuer during validation. |
| `Audience` | — | Required. The `aud` claim and expected audience during validation. |
| `Lifetime` | 15 minutes | Must be positive. Applied to newly issued access tokens. |
| `ClockSkew` | 30 seconds | Must be between 0 and 5 minutes. |
| `Algorithm` | `JwtSigningAlgorithm.HmacSha256` | `HmacSha256` or `RsaSha256`. |
| `SymmetricKey` | — | Required for `HmacSha256`. Must be **at least 32 bytes** (UTF-8 byte count). |
| `RsaPrivateKeyPem` | — | Required for `RsaSha256`. PEM (PKCS#8 or PKCS#1). Must be **at least 2048 bits**. |
| `RsaPublicKeyPem` | — | Optional for `RsaSha256`; derived from the private key if omitted. |
| `ValidateIssuer` / `ValidateAudience` / `ValidateLifetime` | `true` | Toggle individual validation checks. |

`RefreshTokenOptions` (configured via `configureRefreshTokens`, optional):

| Option | Default | Notes |
|---|---|---|
| `Lifetime` | 14 days | Must be positive. |
| `TokenByteLength` | 32 | Must be at least 32 (256 bits of entropy). |
| `ReuseDetectionGrace` | 5 seconds | Must be non-negative. See "Notes / behavior" below. |

## Main types

| Type | Lifetime | Description |
|---|---|---|
| `IJwtTokenGenerator` | Singleton | `GenerateToken(claims, issuedAt?)` — issues a signed JWT from claims. |
| `IJwtTokenValidator` | Scoped | `ValidateAsync(token, ct)` — validates signature, issuer, audience, lifetime; returns a `ClaimsPrincipal?`. |
| `IPasswordHasher` | Singleton (`Pbkdf2PasswordHasher`) | `Hash(password)`, `Verify(hash, password)` → `PasswordVerificationOutcome`. |
| `IRefreshTokenService` | Scoped | `IssueAsync`, `RotateAsync`, `RevokeAsync`. Requires the application to register `IRefreshTokenStore`. |
| `IRefreshTokenStore` | App-provided | `AddAsync`, `FindByHashAsync`, `UpdateAsync`, `TryRevokeActiveAsync`, `CompleteRotationAsync`, `RevokeAllForSubjectAsync`. |
| `IAccessTokenRevocationService` | Scoped | `RevokeAccessTokenAsync(token, ct)` — parses `jti`/`exp` and records the revocation. Requires `IAccessTokenRevocationStore`. |
| `IAccessTokenRevocationStore` | App-provided (optional) | `RevokeAsync(jti, expiresAt, ct)`, `IsRevokedAsync(jti, ct)`. |
| `RefreshToken` | Model | Persisted refresh-token record: `TokenHash`, `Subject`, `CreatedAt`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`. |
| `RefreshTokenResult` | Model | `Token` (raw value, only ever surfaced once) + `Descriptor` (`RefreshToken`). |
| `PasswordVerificationOutcome` | Enum | `Failed`, `Success`, `SuccessRehashNeeded`. |

Minimal `IRefreshTokenStore` implementation (in-memory, for illustration only —
use a real database in production):

```csharp
public sealed class MyRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshToken> _byHash = new();

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(_byHash.GetValueOrDefault(tokenHash));

    public Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<bool> TryRevokeActiveAsync(string tokenHash, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        if (_byHash.TryGetValue(tokenHash, out var token) && token.IsActive(revokedAt))
        {
            token.RevokedAt = revokedAt;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task CompleteRotationAsync(string revokedTokenHash, RefreshToken replacement, CancellationToken ct = default)
    {
        if (_byHash.TryGetValue(revokedTokenHash, out var revoked))
        {
            revoked.ReplacedByTokenHash = replacement.TokenHash;
        }

        _byHash[replacement.TokenHash] = replacement;
        return Task.CompletedTask;
    }

    public Task RevokeAllForSubjectAsync(string subject, CancellationToken ct = default)
    {
        foreach (var token in _byHash.Values.Where(t => t.Subject == subject))
        {
            token.RevokedAt ??= DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}
```

## Notes / behavior

- **Refresh tokens are never stored raw.** Only a SHA-256 hash is persisted;
  the raw value is returned exactly once, from `IssueAsync`/`RotateAsync`.
- **Rotation is a two-step, atomic contract.** `RotateAsync` calls
  `TryRevokeActiveAsync` to compare-and-set the presented token to revoked,
  then `CompleteRotationAsync` to persist the replacement and link
  `ReplacedByTokenHash` on the ancestor — implementations must perform the
  add+link step as a single transaction/critical section, since a crash
  between them would leave a revoked token without a replacement link and
  silently disable reuse detection.
- **Reuse detection with a grace window.** `ReuseDetectionGrace` (default 5
  seconds) tolerates a concurrent double-submit of the same token
  immediately after rotation without punishing the caller. Once that grace
  window has elapsed, presenting an already-rotated token again is treated
  as token theft and calls `RevokeAllForSubjectAsync`, invalidating the
  entire token family for that subject.
- **`IRefreshTokenStore` is optional at registration time.** Applications
  that only need JWT issuance/validation and password hashing can call
  `AddPinqponqIdentity` without ever registering a store. `IRefreshTokenService`
  is registered via a factory (not a type registration) specifically so that
  ASP.NET Core's container validation in Development does not abort startup
  for those applications — the missing store is only reported as an
  `InvalidOperationException` the first time `IRefreshTokenService` is
  resolved.
- **`IAccessTokenRevocationStore` follows the same pattern** for applications
  that want immediate logout (revoke by `jti` before natural `exp`) — it's
  entirely optional, and the error also only surfaces on first resolve of
  `IAccessTokenRevocationService`.
- **`IJwtTokenGenerator` is a singleton**; `IJwtTokenValidator` is **scoped**
  so an application's own scoped `IAccessTokenRevocationStore` can be consumed
  without a captive-dependency problem.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Identity.Otp](../Pinqponq.Identity.Otp/README.md) — one-time
  passcodes over email/SMS, for step-up or passwordless flows.
- [Pinqponq.Auth.Totp](../Pinqponq.Auth.Totp/README.md) — RFC 6238 TOTP-based
  2FA.
- [Pinqponq.Auth.Sso.Abstractions](../Pinqponq.Auth.Sso.Abstractions/README.md)
  and [Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md) —
  external identity providers that can feed into the same JWT/refresh-token
  issuance shown above.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
