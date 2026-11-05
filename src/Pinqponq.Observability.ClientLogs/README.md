# Pinqponq.Observability.ClientLogs

The server end of the `pinqloq-kmp` client (`global.deveng:pinqloq-kmp`). A mobile or web app sends its logs
to your API, and these endpoints write them to Pinqloq as device logs. You choose the route, authorization,
rate limiting and the collection. Size and count limits, key redaction and product fields are optional.

## Install

```bash
dotnet add package Pinqponq.Observability.ClientLogs
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core
- Pinqponq.Observability (installed with this package), registered with `AddPinqponqObservability`: the
  endpoints write through its Pinqloq client and read its `DeviceIdentifierHeader` and `RedactedKeys`.

## Quick start

```csharp
builder.Services.AddPinqponqObservability(builder.Configuration);
builder.Services.AddPinqponqClientLogs(builder.Configuration.GetSection("ClientLogs"));
// optional: builder.Services.AddScoped<IClientLogEnricher, MyClientLogEnricher>();

var app = builder.Build();
app.MapPinqponqClientLogs("/client-logs")              // one log per request (pinqloq-kmp endpoint)
   .AllowAnonymous();
app.MapPinqponqClientLogBatch("/client-logs/batch")    // {"logs": [...]} (pinqloq-kmp batchEndpoint)
   .RequireRateLimiting("my-policy");
```

```json
{
  "ClientLogs": {
    "CollectionName": "myapp_client",
    "MaxEntriesPerBatch": 50,
    "MaxBodyBytes": 65536,
    "MaxFieldsPerEntry": 20,
    "MaxValueLength": 2000,
    "RedactedKeyParts": [ "token", "secret", "password" ]
  }
}
```

Both map methods return a `RouteHandlerBuilder`, so authorization and rate limiting are yours (for example
with `Pinqponq.RateLimiting`). The package sets no policy. Both endpoints answer `204 No Content`.

Add the route to `ObservabilityOptions.ExcludedPathSegments` (for example `"/client-logs"`). Otherwise the
request logging middleware also logs every client log request, with its body, in the HTTP collection.

## What the client sends

An entry is `{"event", "logLevel", "metadata", "detail", "date"}`; `metadata` and `detail` are string maps. A
batch is `{"logs": [entry, ...]}`. A `collectionName` in the body is ignored, because the server chooses the
collection.

| Field | Written as |
|---|---|
| `event` | The record's event. Required: an entry without it refuses the request. |
| `logLevel` | `DEBUG`, `WARNING` or `WARN`, `ERROR`, `FATAL` (any case); anything else is `Information`. |
| `metadata`, `detail` | The record's metadata and detail. A `null` value becomes an empty string. |
| `date` | The record's date. When it is missing, Pinqloq stamps the time the record was queued. |

Every record is a Pinqloq device log (`LogSourceType.Device`) in `CollectionName`. The device identifier is
the value of the `ObservabilityOptions.DeviceIdentifierHeader` request header. The correlation id is the
Pinqloq correlation header, or the request's `TraceIdentifier` when that header is missing.

## Options

| Option | Default | Meaning |
|---|---|---|
| `CollectionName` | none, required | Pinqloq collection of the client logs. A missing value stops the host at startup. |
| `MaxEntriesPerBatch` | not set | Most entries in one batch. |
| `MaxBodyBytes` | not set | Most bytes in one request body. The server reads at most 8 KiB past the limit, then refuses. |
| `MaxFieldsPerEntry` | not set | Most `metadata` and `detail` fields of one entry, counted together. |
| `MaxValueLength` | not set | Longest event name (longer is refused), field value and device identifier (longer is cut). |
| `RedactedKeyParts` | empty | Keys that contain one of these parts (any case) get the value `***REDACTED***`. |

A limit that is not set is not checked. Set the limits for an endpoint that takes anonymous calls. When a
limit is set, the whole request is checked before anything is written, so a refused batch writes nothing.

Keys listed in `ObservabilityOptions.RedactedKeys` are always redacted, compared case-insensitively. The keys that
request logging always hides (`Authorization`, `Cookie`, `Set-Cookie`, `ApiKey`, `X-Api-Key`, and keys containing
`secret`, `token` or `password`) are not hidden here unless you list them: exact keys in `RedactedKeys`, parts in
`RedactedKeyParts`.

## Refused requests

A refused request throws `ClientLogRejectedException` before any entry is written. `Reason` says why
(`InvalidRequest`, `TooManyEntries`, `TooManyFields`, `EventTooLong`, `BodyTooLarge`) and `StatusCode` is 413
for `BodyTooLarge` and 400 for the others. The exception is an `ArgumentException`, so Pinqponq.ErrorHandling
answers it with 400 `bad_request`. To answer with your own status and response code, map it:

```csharp
builder.Services.AddPinqponqErrorHandling(options =>
    options.StatusMappingResolver = exception => exception is ClientLogRejectedException rejected
        ? new ExceptionStatusMapping(rejected.StatusCode, "client_log_rejected")
        : null);
```

## Product fields

An `IClientLogEnricher` changes each record before it is written, with the `HttpContext` at hand: for example
the signed-in user's id from the token in place of a `userId` the client sent, or the time the server received
the log. Every registered enricher runs once per entry, after the request was checked and redacted and before
any entry is written. Values an enricher sets are not redacted or cut.

```csharp
public sealed class UserIdClientLogEnricher : IClientLogEnricher
{
    public void Enrich(HttpContext httpContext, PinqloqLogEntry entry)
    {
        entry.Metadata ??= [];
        entry.Metadata.Remove("userId");
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is not null)
        {
            entry.Metadata["userId"] = userId;
        }
    }
}
```
