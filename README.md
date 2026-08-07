# PinqNugets — Pinqponq shared infrastructure packages

[![CI](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml/badge.svg)](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Moves **generic infrastructure** pieces that were duplicated across 12 backends
(cache, SMS, mail, DB connectivity, messaging, authentication, error handling)
into `Pinqponq.*` NuGet packages in one monorepo. Goals: end **version drift**,
stop shipping the same bugs repeatedly, and replace naming/contract scatter
(e.g. `ISmsService` vs `IGSMService`) with one standard surface (Linear PIN-381).

Packages hold **fixed behavior that wraps an external dependency**;
project-specific business logic (domain repositories, message contracts, OTP-role
rules) is **not** packaged.

- Targets: `net8.0`, `net9.0`, `net10.0` — .NET 8 and later. The list is defined
  in one place (`PinqponqTargetFrameworks` in `Directory.Build.props`).
- Dependency versions are managed centrally via **Central Package Management**
  (`Directory.Packages.props`).
- License: [MIT](LICENSE)

## Packages

| Package | Role |
|---|---|
| **Pinqponq.Identity** | JWT issue/validate (HMAC+RSA), refresh token issue/rotate/revoke, PBKDF2 password hash/verify |
| **Pinqponq.Identity.Otp** | OTP generate/send/verify; mail/sms channel routing; storage interface (`IOtpStore`) |
| **Pinqponq.Auth.Totp** | RFC 6238 TOTP 2FA; `otpauth://` provisioning URI |
| **Pinqponq.Auth.Sso.Abstractions** | `IExternalAuthProvider` contract |
| **Pinqponq.Auth.Sso.Google** | Google id_token validation (`Google.Apis.Auth`) |
| **Pinqponq.Cache** | Redis: get/set/remove/exists, distributed lock, health-check |
| **Pinqponq.Sms** | NetGSM SMS sending (`ISmsSender`) + retry |
| **Pinqponq.Mail** | SMTP mail sending (`IEmailSender`, System.Net.Mail) |
| **Pinqponq.Database.Postgres/Mongo/Mssql** | Connection + (Postgres/Mssql) retry + health-check (no repository/entity) |
| **Pinqponq.Messaging.RabbitMq** | Publish (confirms + mandatory) / consume, reconnect, DLX or MaxRedelivery |
| **Pinqponq.ErrorHandling** | Global exception middleware + standard error contract + Pinqloq-compatible structured logging |

Each package exposes an `AddPinqponqXxx(...)` DI extension and an `XxxOptions` class.

## Quick start

### Identity — JWT, refresh token, password
```csharp
builder.Services.AddPinqponqIdentity(jwt =>
{
    jwt.Issuer = "pinqponq";
    jwt.Audience = "pinqponq-clients";
    jwt.Algorithm = JwtSigningAlgorithm.HmacSha256;
    jwt.SymmetricKey = builder.Configuration["Jwt:SymmetricKey"]; // >= 32 bytes
});
builder.Services.AddScoped<IRefreshTokenStore, MyRefreshTokenStore>(); // storage belongs to the app
```
`IJwtTokenGenerator` (singleton) / `IJwtTokenValidator` (scoped — same lifetime as the
revocation store), `IRefreshTokenService`, `IPasswordHasher`. Only the refresh token
**hash** is stored; `RotateAsync` uses
`TryRevokeActiveAsync` + `CompleteRotationAsync` (add+link atomic).
If a rotated token is presented again **after** `ReuseDetectionGrace` (default 5s),
the subject family is revoked via `RevokeAllForSubjectAsync`
(concurrent double-submit within the grace window does not trigger family revoke).
Access tokens get an automatic `jti`; optional store for logout:

```csharp
builder.Services.AddScoped<IAccessTokenRevocationStore, MyJtiStore>();
// IAccessTokenRevocationService.RevokeAccessTokenAsync(token) → writes jti to the store
// JwtTokenValidator returns null for a revoked jti when a store is registered
```

Apps that only need JWT and password hashing may omit `IRefreshTokenStore`;
a missing store is reported when `IRefreshTokenService` is first resolved.

### Cache — Redis
```csharp
builder.Services.AddPinqponqCache(o => o.ConnectionString = "localhost:6379");
builder.Services.AddHealthChecks().AddPinqponqRedis();

await cache.SetAsync("k", myObj, TimeSpan.FromMinutes(5));
await using var handle = await distributedLock.AcquireAsync(
    "resource",
    TimeSpan.FromSeconds(30),
    new DistributedLockAcquireOptions
    {
        IssueFencingToken = true,
        RenewInterval = TimeSpan.FromSeconds(10), // watchdog; null = off
    });
if (handle.Acquired)
{
    // handle.FencingToken — enforcing it on the DB side belongs to the app
    await handle.TryExtendAsync(TimeSpan.FromSeconds(30));
}
```

### Sms / Mail
```csharp
builder.Services.AddPinqponqSms(o =>
{
    // Legacy GET (default):
    // o.Transport = SmsTransport.GetQuery;
    // o.ApiUrl = "https://api.netgsm.com.tr/sms/send/get/";
    // o.Password = "..."; // travels in the GET query — do not log URLs

    o.Transport = SmsTransport.RestV2; // POST + Basic Auth; default HTTPS when ApiUrl is empty
    o.UserCode = "...";
    o.Password = "...";
    o.MsgHeader = "PINQ";
    // o.AllowNoOp = true; // GetQuery + local only; default false
});
builder.Services.AddPinqponqMail(o =>
{
    o.SmtpHost = "localhost";
    o.SmtpPort = 1025;
    o.FromEmail = "noreply@example.com";
    o.AttachmentRoot = @"D:\secure-attachments"; // required when sending attachments
});

await sms.SendAsync(new SmsMessage { To = "+90555...", Text = "..." });
await mail.SendAsync(new EmailMessage { To = "a@b.com", Subject = "...", Body = "..." });
```

### OTP — mail/sms routing
```csharp
builder.Services.AddPinqponqSms(...);
builder.Services.AddPinqponqMail(...);
builder.Services.AddPinqponqOtp(o =>
{
    o.Ttl = TimeSpan.FromMinutes(3);
    o.MinSendInterval = TimeSpan.FromSeconds(30); // passed to IOtpSendRateLimiter
    o.HashPepper = builder.Configuration["Otp:HashPepper"]!; // >= 32 characters
});
builder.Services.AddScoped<IOtpStore, MyOtpStore>(); // TryConsumeAsync + TryRemoveAsync must be atomic
// Default limiter is a no-op; replace it with Redis etc.:
// builder.Services.AddSingleton<IOtpSendRateLimiter, RedisOtpSendRateLimiter>();

await otp.GenerateAndSendAsync("user@example.com");           // Auto → email
var status = await otp.VerifyAsync("user@example.com", code); // OtpVerifyStatus.Success
```
A sender is only required for the channel you use: an email-only app does not need
to call `AddPinqponqSms`.

### TOTP 2FA
```csharp
builder.Services.AddPinqponqTotp(o => o.Issuer = "Pinqponq"); // ITotpService scoped
builder.Services.AddScoped<ITotpReplayStore, MyTotpReplayStore>(); // ctor dependency
var secret = totp.GenerateSecret();
var uri = totp.GetProvisioningUri(secret, "user@example.com"); // QR → Authenticator
bool ok = await totp.ValidateAsync(secret, userCode, subjectKey: userId);
// Sync Validate still exists (no replay); prefer ValidateAsync in production.
```

### Google SSO
```csharp
builder.Services.AddPinqponqGoogleSso(o =>
{
    o.ClientIds.Add(builder.Configuration["Google:ClientId"]!);
    o.RequireEmailVerified = true; // default
    // o.RequireNonce = true; // for browser OIDC
    // o.HostedDomain = "example.com";
});

var result = await provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(idToken));
if (result.Succeeded) { var email = result.User!.Email; }
// Evaluate Google authority (hd / gmail) before auto account-linking by email.
```

### Database (Postgres/Mongo/Mssql)
```csharp
builder.Services.AddPinqponqPostgres(o => o.ConnectionString = cs);
builder.Services.AddHealthChecks().AddPinqponqPostgres(); // via shared NpgsqlDataSource
await using var conn = await connectionFactory.OpenConnectionAsync(); // Postgres/Mssql: retry
```

### RabbitMQ
```csharp
builder.Services.AddPinqponqRabbitMq(o =>
{
    o.HostName = "rabbit";
    o.UserName = "guest";
    o.Password = "guest";
    // o.UseSsl = true; o.Port = 5671; // AMQPS
});
builder.Services.AddRabbitMqConsumer<MyHandler>(o =>
{
    o.Queue = "chat-messages"; // DLX on by default
    // o.EnableDeadLetter = false; o.MaxRedeliveryCount = 5; // poison limit when DLX is off
});

// Publish: publisher confirms + mandatory — unroutable messages throw.
await publisher.PublishAsync(exchange: "", routingKey: "chat-messages", "payload");
```

### ErrorHandling
```csharp
builder.Services.AddPinqponqErrorHandling();
// ...
app.UsePinqponqErrorHandling(); // at the start of the pipeline
```
Caught exceptions become a standard camelCase `ErrorResponse` with `traceId`/`correlationId`,
and field names are logged in the structured format Pinqloq expects.

## Playground — try the packages and their logs in the browser

`samples/Pinqponq.Playground` is a test console that runs all 13 packages against real
dependencies. Each run shows both the result and the **structured log records** the
package emitted — `ErrorHandling`'s `traceId`/`correlationId` field names are only
verifiable when you see them raw.

```bash
dotnet run --project samples/Pinqponq.Playground   # → http://127.0.0.1:5199
```

No containers start on launch; most scenarios work without Docker. Redis, Postgres,
RabbitMQ, Mongo, MailHog, and SQL Server can be started from the top bar with one
click (via Testcontainers). Details: [samples/README.md](samples/README.md).

## Build & test

Requirements: .NET 10 SDK — enough to build every target. To **run** tests on each
target you also need the .NET 8 and .NET 9 runtimes. Integration tests need a working
Docker environment (Testcontainers: Redis, RabbitMQ, PostgreSQL, MongoDB, SQL Server,
MailHog).

```bash
dotnet build -c Release   # net8.0 + net9.0 + net10.0
dotnet test -c Release --collect:"XPlat Code Coverage"
dotnet pack -c Release    # .nupkg per package (one consistent version via CPM)
```

> Note: Dependency versions are defined in one place in `Directory.Packages.props`.
> Integration tests are marked with `[Trait("Category", "Integration")]`.

## Contributing & security

- Contributing guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Security vulnerability reporting: [SECURITY.md](SECURITY.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
