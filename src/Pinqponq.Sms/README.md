# Pinqponq.Sms

SMS sending wrapper for [NetGSM](https://www.netgsm.com.tr/) with a single standard
`ISmsSender` interface — replacing the divergent `ISmsService` / `IGSMService` /
`INetGSMService` contracts that predate this package. Supports both NetGSM's legacy
GET query API and its REST v2 POST API, with built-in retry for transient HTTP
failures.

## Install

```bash
dotnet add package Pinqponq.Sms
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A NetGSM account (`usercode` / `password`) for either the legacy GET API or REST v2

## Quick start

```csharp
using Pinqponq.Sms;
using Pinqponq.Sms.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqSms(options =>
{
    options.ApiUrl = "https://api.netgsm.com.tr/sms/send/get/";
    options.UserCode = builder.Configuration["Sms:UserCode"];
    options.Password = builder.Configuration["Sms:Password"];
    options.MsgHeader = "MYBRAND";
});

var app = builder.Build();
```

Send a message from anywhere `ISmsSender` is injected:

```csharp
public sealed class NotificationService(ISmsSender smsSender)
{
    public Task NotifyAsync(string phoneNumber, CancellationToken cancellationToken) =>
        smsSender.SendAsync(
            new SmsMessage { To = phoneNumber, Text = "Your order has shipped." },
            cancellationToken);
}
```

## Configuration

`AddPinqponqSms(Action<SmsOptions> configure)` registers `ISmsSender` (as
`NetGsmSmsSender`) together with a named `HttpClient`, and validates `SmsOptions` on
startup via `ValidateOnStart()`.

| Option | Default | Notes |
|---|---|---|
| `ApiUrl` | `null` | For `SmsTransport.GetQuery`, the legacy send endpoint, e.g. `https://api.netgsm.com.tr/sms/send/get/`. For `SmsTransport.RestV2`, an empty value resolves to `SmsOptions.DefaultRestV2ApiUrl` (`https://api.netgsm.com.tr/sms/rest/v2/send`). Must be an absolute **HTTPS** URL when set. |
| `Transport` | `SmsTransport.GetQuery` | See [Main types](#main-types) below. |
| `UserCode` | `null` | NetGSM `usercode`. Required whenever an `ApiUrl` is in effect (i.e. always for `RestV2`; for `GetQuery`, whenever sending isn't a no-op). |
| `Password` | `null` | NetGSM `password`. Same requirement as `UserCode`. |
| `MsgHeader` | `null` | Sender header (`msgheader`) shown to the recipient. Sent as an empty string if not set. |
| `RetryCount` | `3` | Maximum retry attempts on transient HTTP failures. Must not be negative. |
| `RetryBaseDelay` | `300ms` | Base delay for exponential backoff between retries (with jitter). Must be positive. |
| `HttpTimeout` | `30s` | Timeout applied to the underlying `HttpClient`. Must be positive. |
| `AllowNoOp` | `false` | Only meaningful for `GetQuery`: when `true`, an empty `ApiUrl` is allowed and `SendAsync` becomes a no-op. **Local development only** — see [Notes / behavior](#notes--behavior). Ignored for `RestV2`. |

Options are validated eagerly at startup by `SmsOptionsValidator` (registered as an
`IValidateOptions<SmsOptions>`), so a misconfigured `ApiUrl`, missing credentials for
the selected transport, or an invalid `Transport` value fails fast instead of at first
send.

## Main types

- **`AddPinqponqSms`** — `IServiceCollection` extension that registers `ISmsSender`
  and its options.
- **`ISmsSender`** — the single sending contract: `Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)`.
- **`SmsMessage`** — `To` (recipient; non-digit characters are stripped before
  sending) and `Text` (message body). Both `required`.
- **`SmsOptions`** — configuration bound/validated as described above.
- **`SmsTransport`** — chooses how `NetGsmSmsSender` talks to NetGSM:
  - `GetQuery` (default, `= 0`) — legacy NetGSM GET request with `usercode` and
    `password` on the query string.
  - `RestV2` (`= 1`) — NetGSM REST v2 `POST` with a JSON body and HTTP **Basic
    Auth** (`usercode:password` base64-encoded in the `Authorization` header). When
    `ApiUrl` is empty, the request goes to the default HTTPS endpoint.
- **`NetGsmSmsSender`** — the `ISmsSender` implementation. Exposes
  `HttpClientName` (`"Pinqponq.Sms.NetGsm"`), the name of the `HttpClient` registered
  by `AddPinqponqSms` (useful if you want to further customize it via
  `IHttpClientBuilder`, e.g. add a message handler).
- **`NetGsmRejectedException`** — thrown when NetGSM accepts the HTTP call (HTTP
  200) but returns a business-error body (NetGSM's response code conventions treat a
  leading `00` as success; anything else — e.g. code `30` — is a rejection). Exposes
  `ResponseBody` with the raw provider response.

## Notes / behavior

- **`AllowNoOp` is a local-only escape hatch.** It only applies to
  `SmsTransport.GetQuery`: when `ApiUrl` is empty *and* `AllowNoOp` is `true`,
  `SendAsync` returns immediately without making a network call. This exists so a
  development environment without real NetGSM credentials can still boot with
  `AddPinqponqSms` wired up. It is ignored for `RestV2` — an empty `ApiUrl` there
  always resolves to the default REST endpoint and a real call is made. Do not set
  `AllowNoOp = true` outside local development.
- **The GET transport puts your password in the URL.** `SmsTransport.GetQuery`
  sends `usercode` and `password` as query-string parameters. **Never log request
  URLs** (or enable `HttpClient` logging handlers that do) while using this
  transport — the credentials would end up in plaintext logs. Prefer
  `SmsTransport.RestV2` (Basic Auth over HTTPS, credentials in a header, not the
  URL) for new integrations.
- **Recipient numbers are normalized.** `SmsMessage.To` has all non-digit
  characters stripped before the request is built; if nothing is left, `SendAsync`
  throws `ArgumentException`.
- **Retries use [Polly](https://github.com/App-vNext/Polly) with exponential
  backoff and jitter**, governed by `RetryCount` / `RetryBaseDelay`. Transient
  `HttpRequestException`s and non-caller-initiated `TaskCanceledException`s
  (timeouts) are retried.
- **`NetGsmRejectedException` is never retried.** A business rejection (e.g. bad
  credentials, insufficient credit, invalid recipient — anything NetGSM reports
  with a non-`00` code while still answering HTTP 200) is a permanent failure, not
  a transient one, so the retry pipeline lets it propagate immediately.
- **Missing credentials fail fast per-send**, not just at startup: `SendAsync`
  throws `InvalidOperationException` if `UserCode` or `Password` is blank at call
  time (in addition to the startup validation already rejecting most such
  configurations).
- The registered `HttpClient`'s `Timeout` is read from `SmsOptions.HttpTimeout` at
  the time the client is configured (via `ConfigureHttpClient`), so it reflects
  whatever `IOptions<SmsOptions>` resolves to at that point.

## Related packages

- [`Pinqponq.Identity.Otp`](../Pinqponq.Identity.Otp) — sends one-time passwords over
  SMS (and email) using this package's `ISmsSender` as its SMS delivery channel.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
