# Pinqponq.Observability

[Pinqloq](https://www.nuget.org/packages/pinqloq) logging for ASP.NET Core:

- **Request logging** — one Pinqloq record per request with method, path, status, duration,
  redacted query and request/response bodies, and an event name built from the route.
- **`ILogService`** — application and background job logs to both `ILogger` and Pinqloq, with
  exception fields and stack trace trimming.
- **Client-log relay** — an endpoint that forwards logs sent by mobile/web clients to Pinqloq.

## Install

```bash
dotnet add package Pinqponq.Observability
```

## Quick start

```json
{
  "Observability": {
    "HttpCollectionName": "myapp_http",
    "JobsCollectionName": "myapp_jobs",
    "ClientCollectionName": "myapp_client"
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
app.MapPinqponqClientLogs("/api/myapp/client-logs");
```

## Configuration (`Observability` section)

| Option | Default | Notes |
|---|---|---|
| `HttpCollectionName` | — | **Required.** Collection of the per-request records. |
| `JobsCollectionName` | — | **Required.** Collection of `ILogService` records that name no collection; also Pinqloq's `ApiLogsCollectionName`. |
| `ClientCollectionName` | — | **Required.** Collection of the relayed client logs. |
| `MaxLoggedBodyBytes` | 5 MiB | Longer bodies are trimmed and marked `[truncated: …]`. |
| `RequireDeviceUid` | `false` | Rejects a request without a `DeviceUid` header/query value with 400 `DEVICE_UID_REQUIRED`. |
| `DeviceIdentifierHeader` | `Identifier` | Header whose value becomes the record's device identifier. |
| `ExcludedPathSegments` | `/health`, `/openapi`, `/scalar`, `/swagger`, `/client-logs` | Paths containing one of these are not logged. |
| `DeviceUidExemptPathSegments` | none | Paths that skip the device uid check (e.g. third-party webhooks). |

Missing collection names fail the application at startup, and so does a missing
`Pinqloq:SecretKey` (the Pinqloq client validates it). Binding a list from configuration adds to
its defaults.

## Request records

- **Event name:** `<method>_<route template>`, e.g. `get_api_orders_id` for
  `GET api/orders/{id}`; the raw path when the route has a catch-all parameter or no endpoint
  matched. When Pinqponq.ErrorHandling answered the request, its event name is used instead
  (`exception_<Type>`, `request_blocked_maintenance_mode`, `request_cancelled_by_client`) and
  its detail is added under `exception` (via `Pinqponq.Contracts.IRequestErrorFeature`).
- **Level:** 2xx/3xx Information, 4xx Warning, 5xx Error.
- **Fields:** `traceId`, `RequestMethod`, `path`, `ResponseCode` (HTTP status), `elapsedMs`,
  `query`, `InputJson`, `OutputJson`, `projectId`/`projectKey`/`keyId` when set in
  `HttpContext.Items`.
- **Redaction:** keys such as `Authorization`, `Password`, `AccessToken`, `RefreshToken`,
  `ApiKey` and anything containing `secret`, `token` or `password` become `***REDACTED***`;
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

## Client-log relay

`MapPinqponqClientLogs(route)` maps `POST route` (default `/api/client-logs`; keep `/client-logs` in a
custom route, so request logging does not log the relay itself) with a `ClientLogRequest` body (`event`,
`logLevel`, `metadata`, `detail`, `date`) and answers 204. The record goes to
`ClientCollectionName` as a device log with the `DeviceIdentifierHeader` value.

## Repository

https://github.com/pinqponq/pinqnuqets
