# Pinqponq.Contracts

Response envelope, paging models, common error codes and the request error feature shared by
every Pinqponq API and by the Pinqponq.ErrorHandling / Pinqponq.Observability middlewares.

## Install

```bash
dotnet add package Pinqponq.Contracts
```

## Response envelope

`BaseResponseModel` is the shape of every response, serialized camelCase:

```json
{ "status": false, "statusCode": 400, "message": "The request was invalid.", "responseCode": "general_error" }
```

| Field | Type | Notes |
|---|---|---|
| `status` | bool | `true` on success, `false` on an error. |
| `statusCode` | int | HTTP status code. |
| `message` | string | Human-readable text. **Always set on an error.** |
| `responseCode` | string | Stable machine-readable code. |

### Compatibility with deveng-networking-kmp

The mobile apps decode every non-2xx body into deveng-networking-kmp's
`ErrorResponse(val message: String)` with `ignoreUnknownKeys = true`, and hand `message` to the
app's exception handler. So an error body is compatible when:

- it is a JSON object with a non-null string `message` (a missing or null `message` fails the
  decode and the app shows its generic "Unknown error");
- any other field is allowed and ignored by the client. `status`, `statusCode`, `responseCode`
  and Pinqponq.ErrorHandling's optional `traceId` are safe to add.

`ErrorResponse` of Pinqponq.ErrorHandling writes the same four fields in the same order (plus
`traceId` unless turned off), so APIs using either type stay compatible.

## Paging

```csharp
[HttpGet]
public ActionResult<PagedResponseModel<List<OrderDto>>> List([FromQuery] PagedRequestModel paging) =>
    orders.ToPagedResponse(paging.Page, paging.Size, page => page.Select(ToDto).ToList(),
        orderBy: rows => rows.OrderByDescending(order => order.CreatedAt));
```

- `PagedRequestModel` binds `?PageNumber=` (default 1) and `?PageSize=` (default 10).
- `PagedResponseModel<T>` extends `BaseResponseModel` with `page`, `size`, `rowCount`,
  `pageCount`, `hasNextPage`, `firstRowOnPage`, `lastRowOnPage` and `data`.
- `ToPagedResponse` pages an in-memory sequence. A page number past the last page returns the
  last page; a page number or page size under 1 throws `ArgumentException`. Page a database
  query in the query itself instead of materializing it first.

## Common error codes

`CommonErrorCodes`: `general_error`, `api_client_closed_request`,
`api_body_required`, `api_page_size_invalid`, `api_page_number_invalid`. Product-specific codes
stay in the product. Pinqponq.ErrorHandling ships the localized messages of the codes it can
return.

## Request error feature

`IRequestErrorFeature` (default implementation `RequestErrorFeature`) is an ASP.NET Core
`HttpContext.Features` entry: Pinqponq.ErrorHandling sets it when it answers a failed request
(`EventName`, e.g. `exception_KeyNotFoundException`, and a `Detail` dictionary), and
Pinqponq.Observability's request logging reads it, so a failed request still produces one log
record. It lives here so neither middleware package depends on the other.

## Repository

https://github.com/pinqponq/pinqnuqets
