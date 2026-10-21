# Pinqponq.RateLimiting

ASP.NET Core rate limiting set up in one call. You name the policies and their numbers; the
package registers the limiters, partitions requests by client address, user or your own key,
and answers a rejected request with `429 Too Many Requests`, a `Retry-After` header and the
standard error body.

## Install

```bash
dotnet add package Pinqponq.RateLimiting
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core (the package uses the built-in rate limiting middleware)
- Pinqponq.ErrorHandling (installed with this package) for the error body

## Quick start

```csharp
builder.Services.AddPinqponqRateLimiting(options =>
{
    options.Policies.Add("sign-in", new RateLimitPolicyOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(5),
        PartitionBy = RateLimitPartitionSource.UserOrClientAddress,
    });
    options.GlobalLimits.Add(new RateLimitPolicyOptions
    {
        PermitLimit = 600,
        Window = TimeSpan.FromMinutes(1),
    });
});

var app = builder.Build();
app.UseForwardedHeaders();   // first, so the client address is the real one behind a proxy
app.UseRouting();
app.UseRateLimiter();        // after UseRouting, so endpoint policies are found
app.MapControllers();
```

Apply a named policy with the standard ASP.NET Core attribute or endpoint method:

```csharp
[EnableRateLimiting("sign-in")]
public sealed class SignInController : ControllerBase { /* ... */ }

app.MapPost("/sign-in", SignIn).RequireRateLimiting("sign-in");
```

The numbers above are only an example. The package has no default limit or window: each
product chooses its own.

## Configuration

`AddPinqponqRateLimiting(Action<RateLimitingOptions>)` sets the options in code.
`AddPinqponqRateLimiting(IConfiguration section, Action<RateLimitingOptions>? configure)` binds
them from a configuration section (for example `RateLimiting`) and then runs `configure`, for
settings that configuration cannot hold, such as `PartitionKeyResolver`.

```json
{
  "RateLimiting": {
    "Policies": {
      "sign-in": { "PermitLimit": 10, "Window": "00:05:00", "PartitionBy": "UserOrClientAddress" }
    },
    "GlobalLimits": [
      { "PermitLimit": 600, "Window": "00:01:00" }
    ]
  }
}
```

Define `GlobalLimits` in one settings file only: .NET merges a list from two settings files
element by element.

| `RateLimitingOptions` | Default | Notes |
|---|---|---|
| `Policies` | empty | Named policies for `[EnableRateLimiting(name)]` and `RequireRateLimiting(name)`. |
| `GlobalLimits` | empty | Limits applied to every request. With several, a request must be allowed by each of them. |
| `UserIdClaimType` | `ClaimTypes.NameIdentifier` | Claim that identifies the user. The JWT bearer handler maps `sub` to this claim. |

| `RateLimitPolicyOptions` | Default | Notes |
|---|---|---|
| `PermitLimit` | none | Requests allowed per partition in one window. Required, greater than 0. |
| `Window` | none | Length of the fixed window. Required, greater than 0. |
| `PartitionBy` | `ClientAddress` | What requests are counted by (see below). |
| `PartitionKeyResolver` | none | `Func<HttpContext, string?>` for `Custom`. Required for `Custom`. |
| `IsPartitionKeySecret` | `false` | Keep the `Custom` value only as its SHA-256 digest. Set it for API keys and other secrets. |

Every setting is validated at startup. The application does not start when a limit or window is
not greater than 0, a policy name is empty, a `Custom` policy has no resolver, or
`UserIdClaimType` is empty.

A request over a limit is rejected at once. It is never queued. The global limits are checked
before the named policy, so a request counts against the global limits also when its named
policy rejects it.

## Partitions

Each distinct value gets its own budget. Values from different sources never share a budget,
even when their text is the same.

| `PartitionBy` | Counted by | When the value is missing |
|---|---|---|
| `ClientAddress` | `HttpContext.Connection.RemoteIpAddress` | One shared partition for all requests without an address |
| `User` | The `UserIdClaimType` claim | The request is not limited by this policy. Use it only on endpoints that require authentication. |
| `UserOrClientAddress` | The user, otherwise the client address | Client address |
| `Custom` | The value `PartitionKeyResolver` returns | Client address |

A request without a client address is counted in one shared partition. The package logs a warning
the first time this happens, because it usually means `UseForwardedHeaders` is missing or comes
after `UseRateLimiter`.

### A limit per API key and per address

A key sent in a request header identifies a tenant, but a caller can send a different key with
every request. Chain a limit per key with a looser limit per client address, so changing the key
does not buy a new budget:

```csharp
builder.Services.AddPinqponqRateLimiting(options =>
{
    options.GlobalLimits.Add(new RateLimitPolicyOptions
    {
        PermitLimit = 100,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = RateLimitPartitionSource.Custom,
        PartitionKeyResolver = context => context.Request.Headers["X-Api-Key"].FirstOrDefault(),
        IsPartitionKeySecret = true,
    });
    options.GlobalLimits.Add(new RateLimitPolicyOptions
    {
        PermitLimit = 500,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = RateLimitPartitionSource.ClientAddress,
    });
});
```

## Rejected requests

A rejected request gets:

- status `429`;
- `Retry-After`: the seconds until the window ends, rounded up and at least 1. The header is left
  out only when the limiter gives no time;
- the standard error body of Pinqponq.ErrorHandling with the response code
  `too_many_requests` (`CommonErrorCodes.TooManyRequests`):

```json
{ "status": false, "statusCode": 429, "message": "Too many requests. Try again later.", "responseCode": "too_many_requests", "traceId": "..." }
```

With `AddPinqponqErrorLocalization()` the message comes from the error message catalog in the
language of the request. The failure is also described on `IRequestErrorFeature` with the event
name `request_rate_limited` (`RateLimitingEvents.RequestRateLimited`), so Pinqponq.Observability's
request log record carries it.

To return another body, register your own `IRateLimitRejectionWriter`. The status code and the
`Retry-After` header are already set when it runs:

```csharp
builder.Services.AddSingleton<IRateLimitRejectionWriter, PlainRejectionWriter>();

internal sealed class PlainRejectionWriter : IRateLimitRejectionWriter
{
    public Task Write(HttpContext context, int? retryAfterSeconds, CancellationToken cancellationToken) =>
        context.Response.WriteAsJsonAsync(new { error = "rate_limited", retryAfter = retryAfterSeconds }, cancellationToken);
}
```

## What stays in the product

Which endpoints get which policy, the limits and windows, the claim types and header names that
identify a tenant, and any product-specific body.

## Related packages

- [Pinqponq.ErrorHandling](../Pinqponq.ErrorHandling/README.md) — the error body and the message catalog
- [Pinqponq.Contracts](../Pinqponq.Contracts/README.md) — `CommonErrorCodes.TooManyRequests`

## Repository

[github.com/pinqponq/pinqnuqets](https://github.com/pinqponq/pinqnuqets)
