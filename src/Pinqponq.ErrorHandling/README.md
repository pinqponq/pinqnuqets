# Pinqponq.ErrorHandling

Global exception-handling middleware for ASP.NET Core, plus a standard, camelCase
`ErrorResponse` contract. Every unhandled exception becomes a consistent JSON error
body and a single structured log record — using field names compatible with
Pinqloq's log pipeline — instead of an ad-hoc stack trace or a framework default
problem-details page.

## Install

```bash
dotnet add package Pinqponq.ErrorHandling
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core (the package uses `<FrameworkReference Include="Microsoft.AspNetCore.App" />`,
  so it targets web applications, not plain console/worker apps)

## Quick start

```csharp
using Pinqponq.ErrorHandling;
using Pinqponq.ErrorHandling.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqErrorHandling();

var app = builder.Build();

// Register first, so it wraps every other middleware and can catch
// exceptions raised anywhere downstream in the pipeline.
app.UsePinqponqErrorHandling();

app.MapGet("/orders/{id}", (int id) =>
{
    throw new KeyNotFoundException($"Order {id} was not found.");
});

app.Run();
```

A request to the endpoint above returns HTTP 404 with:

```json
{
  "status": false,
  "statusCode": 404,
  "message": "The requested resource was not found.",
  "responseCode": "not_found",
  "traceId": "0HN7...:00000001"
}
```

## Configuration

`AddPinqponqErrorHandling(Action<ErrorHandlingOptions>? configure = null)` registers
`ErrorHandlingOptions` (unvalidated — there's no required field). `UsePinqponqErrorHandling()`
adds `ExceptionHandlingMiddleware` to the pipeline via `app.UseMiddleware<...>()`.
**Call `UsePinqponqErrorHandling()` as early as possible in the pipeline** — ideally
the very first `app.Use...` call — so it wraps everything else and can catch
exceptions from routing, authentication, and every other middleware/endpoint
downstream of it.

| Option | Default | Notes |
|---|---|---|
| `CorrelationIdHeader` | `"X-Correlation-ID"` | Inbound request header read to resolve a correlation id. |
| `IncludeExceptionMessage` | `false` | When `false` (recommended for production), the response `message` is a generic, status-derived string (e.g. "The requested resource was not found."), never the raw `Exception.Message` — so internal details never leak to a client. Set to `true` only in trusted/diagnostic environments. |
| `StatusMappingResolver` | `null` | Optional `Func<Exception, ExceptionStatusMapping?>` to override the built-in status/response-code mapping for specific exception types. Return `null` from the delegate to fall back to the built-in mapping for that exception. If the resolver itself throws, the error is logged and the built-in mapping is used. |

Example with a custom mapping for a domain exception:

```csharp
builder.Services.AddPinqponqErrorHandling(options =>
{
    options.StatusMappingResolver = exception => exception switch
    {
        OrderAlreadyShippedException => new ExceptionStatusMapping(
            StatusCodes.Status409Conflict, "order_already_shipped"),
        _ => null,
    };
});
```

## Localized error responses

```csharp
builder.Services.AddPinqponqErrorHandling();
builder.Services.AddPinqponqErrorLocalization();
```

With localization on, the response code from the status mapping above (or from an
`IExceptionResolver`) is looked up in the message catalog and the message is returned in the
language of the `LanguageHeader` request header (`Accept-Language` by default; `tr`, `de-DE`, … —
case-insensitive). A mapped code the catalog does not know (`not_found`, …) is kept and gets the
`FallbackResponseCode` (`general_error`) message; a resolver code the catalog does not know becomes
`FallbackResponseCode`. The status always comes from the mapping or the resolver.

| `ErrorLocalizationOptions` | Default | Notes |
|---|---|---|
| `LanguageHeader` | `Accept-Language` | Header carrying the language; its first tag is used, and `pt-BR` falls back to `PT` when there is no `PT-BR` catalog. |
| `DefaultLanguage` | `EN` | Used when the header is missing or the catalog has no message in that language. |
| `FallbackResponseCode` | `general_error` | Code for an unknown resolver code; its message also serves an unknown mapped code. |
| `DefaultMessage` | `An unexpected error occurred.` | Used when the catalog has no message at all. |
| `CatalogDirectory` | `Localization` | The application's catalog, relative to `AppContext.BaseDirectory`: one `<LANGUAGE>.json` per language, each a `{ "code": "message" }` object. Its entries add codes and override the embedded ones. A file that cannot be read is logged and skipped. |
| `CatalogAssemblies` | this package | Assemblies whose embedded `*.Localization.<LANGUAGE>.json` resources form the built-in catalog. This package ships `general_error`, `api_body_required`, `api_page_size_invalid` and `api_page_number_invalid` in AR, DE, EN, ES, FR, HI, ID, IT, JA, KO, PT and TR; Pinqponq.ErrorHandling.Postgres adds the `database_*` codes. |

## Exception resolvers

An `IExceptionResolver` handles the exceptions of one technology before `StatusMappingResolver`
and the built-in table. Register it with `AddPinqponqExceptionResolver<T>()`; the first resolver
that returns an `ExceptionResolution` wins, and a resolver that throws is logged and skipped.
An `ExceptionResolution` names the status and response code, an optional `FallbackResponseCode`, the `ReportedException` that names the log event (e.g.
the driver exception inside an ORM exception) and extra `LogDetail` fields.
With localization on, a resolver code the catalog does not know becomes the resolution's
`FallbackResponseCode`, or the options' one.
[Pinqponq.ErrorHandling.Postgres](../Pinqponq.ErrorHandling.Postgres/README.md) is one.

## Request error feature

Every answered failure — a handled exception or a request the client closed — is
described on `HttpContext.Features` as `Pinqponq.Contracts.IRequestErrorFeature`:

- `EventName`: `exception_<ExceptionType>` or `request_cancelled_by_client` (constants in `ErrorHandlingEvents`);
- `Detail`: `traceId`, `method`, `statusCode`, `errorCode`, `errorMessage`, `language` (with
  localization on), `queryString` (when present), then the resolver's `LogDetail`.

Pinqponq.Observability's request logging reads it, so a failed request still produces a single
Pinqloq record that carries the error. Nothing is set when the response had already started.

## Main types

- **`AddPinqponqErrorHandling`** / **`UsePinqponqErrorHandling`** — `IServiceCollection`
  / `IApplicationBuilder` extensions that register options and the middleware.
- **`ExceptionHandlingMiddleware`** — catches unhandled exceptions from the rest of
  the pipeline, logs a structured record, and writes the `ErrorResponse` JSON body.
  Also handles client-disconnect (`OperationCanceledException` when
  `HttpContext.RequestAborted` was signaled) by setting HTTP 499 without producing an
  error body; the console log entry for that case is `Information`.
- **`ErrorHandlingOptions`** — configuration described above.
- **`ErrorResponse`** — the response contract, serialized camelCase:
  `status` (always `false`), `statusCode`, `message`, `responseCode`, `traceId`.
- **`ExceptionStatusMapping`** — a `readonly record struct(int StatusCode, string ResponseCode)`
  returned by a custom `StatusMappingResolver`.
- **`ExceptionMapping`** *(internal)* — the built-in exception → (status code,
  response code) table:

  | Exception type | Status | Response code |
  |---|---|---|
  | `UnauthorizedAccessException` | 401 | `unauthorized` |
  | `KeyNotFoundException` | 404 | `not_found` |
  | `ArgumentException` (and subtypes, e.g. `ArgumentNullException`) | 400 | `bad_request` |
  | `FormatException` | 400 | `bad_request` |
  | `InvalidOperationException` | 400 | `bad_request` |
  | `NotImplementedException` | 501 | `not_implemented` |
  | `TimeoutException` | 504 | `timeout` |
  | Any type whose name contains `"NotFound"` | 404 | `not_found` |
  | Everything else | 500 | `internal_error` |

  `OperationCanceledException` (not aborted by the client) is mapped separately, to
  504 / `timeout`, before this table is even consulted.

## Notes / behavior

- **Response `message` is generic by default.** With `IncludeExceptionMessage = false`
  (the default), the body's `message` comes from a small status-code-keyed table of
  user-safe strings (e.g. 400 → "The request was invalid.", 401 → "Authentication is
  required.", 500 → "An unexpected error occurred."), never from `exception.Message`.
- **Correlation id / trace id — `X-Correlation-ID` behavior.** The middleware resolves
  a correlation id per request: if the inbound request has a non-empty
  `CorrelationIdHeader` (default `X-Correlation-ID`), that header's value is used;
  otherwise it falls back to `HttpContext.TraceIdentifier`. The response body's
  `traceId` field is set to this **resolved correlation id**. The structured log
  record additionally includes the request's own `TraceId` (always
  `HttpContext.TraceIdentifier`, regardless of the header) as a separate field. This
  means:
  - **No `X-Correlation-ID` header sent** → response `traceId` and the log's `TraceId`
    are the same value (both `HttpContext.TraceIdentifier`).
  - **`X-Correlation-ID` header sent** → response `traceId` and the log's
    `CorrelationId` field both equal the header value, while the log's own `TraceId`
    field remains the request's ASP.NET Core `TraceIdentifier` — the two intentionally
    differ, so you can still find the record by either the caller-supplied
    correlation id or the request's native identity.
- **Structured logging is Pinqloq-compatible.** Each handled exception produces
  exactly one log entry (`LogLevel.Error` for 5xx, `LogLevel.Warning` for 4xx) with a
  message template and named placeholders — `{ResponseCode}`, `{StatusCode}`,
  `{Method}`, `{Path}`, `{TraceId}`, `{CorrelationId}` — rather than an interpolated
  string, so the fields remain queryable/structured in whatever log sink is
  configured (Pinqloq or otherwise).
- **The response is only written if it hasn't started.** If `HttpContext.Response.HasStarted`
  is already `true` (e.g. streaming had begun), the middleware still logs the
  exception but skips writing a body — the connection is left as-is rather than
  throwing a second exception trying to write over a started response.
- **A failure to write the error response is itself logged** (`LogError`) rather than
  allowed to propagate, so a serialization or I/O problem while producing the error
  body cannot itself crash the pipeline a second time.
- **Client disconnects are not treated as errors.** A canceled request
  (`OperationCanceledException` while `RequestAborted.IsCancellationRequested`) sets
  HTTP 499 (if the response hasn't started) and produces no `ErrorResponse` body and
  only an `Information` log entry — this is expected client behavior, not a server fault.

## Related packages

This package depends only on `Pinqponq.Contracts` (for `IRequestErrorFeature` and
`CommonErrorCodes`). It is commonly combined with Pinqponq.Observability (request logging that
carries the error detail) and Pinqponq.ErrorHandling.Postgres (database error mapping).

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
