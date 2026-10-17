# Pinqponq.Observability

[Pinqloq](https://www.nuget.org/packages/pinqloq) logging for ASP.NET Core:

- **Request logging** — one Pinqloq record per request with method, path, status, duration,
  redacted query and request/response bodies, and an event name built from the route.
- **`ILogService`** — application and background job logs to both `ILogger` and Pinqloq, with
  exception fields and stack trace trimming.

## Install

```bash
dotnet add package Pinqponq.Observability
```

## Quick start

```json
{
  "Observability": {
    "HttpCollectionName": "myapp_http",
    "JobsCollectionName": "myapp_jobs"
  },
  "Pinqloq": { "SecretKey": "..." }
}
```

```csharp
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.Observability.DependencyInjection;

builder.Services.AddPinqponqObservability(builder.Configuration);
builder.Services.AddPinqponqErrorHandling();

var app = builder.Build();

app.UsePinqponqRequestLogging();   // outer: sees the final status and body
app.UsePinqponqErrorHandling();    // inner: turns exceptions into error responses
```

## Configuration (`Observability` section)

| Option | Default | Notes |
|---|---|---|
| `HttpCollectionName` | — | **Required.** Collection of the per-request records. |
| `JobsCollectionName` | — | **Required.** Collection of `ILogService` records that name no collection; also Pinqloq's `ApiLogsCollectionName`. |
| `MaxLoggedBodyBytes` | 5 MiB | Longer bodies are trimmed and marked `[truncated: …]`. |
| `DeviceIdentifierHeader` | none | Header whose value becomes the record's device identifier; unset, records carry none. |
| `RedactedKeys` | none | Keys redacted on top of the built-in ones (see Redaction). |
| `LoggedContextItems` | none | `HttpContext.Items` key → record field, added to the request record when set. |
| `ExcludedPathSegments` | `/health`, `/openapi`, `/scalar`, `/swagger` | Paths containing one of these are not logged. |

Missing collection names fail the application at startup, and so does a missing
`Pinqloq:SecretKey` (the Pinqloq client validates it). Binding a list from configuration adds to
its defaults.

## Request records

- **Event name:** `<method>_<route template>`, e.g. `get_api_orders_id` for
  `GET api/orders/{id}`; the raw path when the route has a catch-all parameter or no endpoint
  matched. When Pinqponq.ErrorHandling answered the request, its event name is used instead
  (`exception_<Type>`, `request_cancelled_by_client`) and
  its detail is added under `exception` (via `Pinqponq.Contracts.IRequestErrorFeature`).
- **Level:** 2xx/3xx Information, 4xx Warning, 5xx Error.
- **Fields:** `traceId`, `RequestMethod`, `path`, `ResponseCode` (HTTP status), `elapsedMs`,
  `query`, `InputJson`, `OutputJson`, and the `LoggedContextItems` fields.
- **Redaction:** `Authorization`, `Cookie`, `Set-Cookie`, `ApiKey`, `X-Api-Key`, the
  `RedactedKeys` and anything containing `secret`, `token` or `password` become `***REDACTED***`;
  URL values become `****`; Pinqloq's attribute-based redaction plan is applied too. Multipart,
  non-text and OpenAPI bodies are omitted.
- **Correlation id:** Pinqloq's correlation header when the caller sent one, otherwise the
  trace identifier.

## `ILogService`

The interface ships in [Pinqponq.Observability.Abstractions](../Pinqponq.Observability.Abstractions/README.md)
(no dependencies), so domain and application projects can log without this package.

```csharp
logService.Error("Export failed", exception, new Dictionary<string, object?> { ["orderId"] = id },
    eventName: "order_export_failed");
```

`data` entries become the record's metadata and detail (non-string values as JSON; a `path`
entry becomes the record's path). An exception adds `exceptionType`, `exceptionMessage`,
`stackTrace` (first 4,000 characters) and the inner exception's type and message; an
`AggregateException` adds `aggregatedExceptionCount` and the first failure's detail. A record
without `collectionName` goes to `JobsCollectionName`. A Pinqloq delivery failure is logged as
an `ILogger` warning.

## Repository

https://github.com/pinqponq/pinqnuqets
