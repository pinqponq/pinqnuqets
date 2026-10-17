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
- it may have other fields, such as `status`, `statusCode` and `responseCode`.
  Pinqponq.ErrorHandling always adds `traceId`. The client ignores unknown fields, so adding
  it is safe.

`ErrorResponse` of Pinqponq.ErrorHandling writes the same four fields in the same order, plus
`traceId`. So APIs using either type stay compatible.

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
- `ToPagedResponse` on an `IEnumerable<T>` takes the requested page from rows that are already in
  memory. On an `IQueryable<T>` it counts the rows of the query and reads only the requested page.
- For a database query, page it in the database. Do not load all rows into memory first.
- A page number under 1 throws `ArgumentException` with `api_page_number_invalid` as the message.
  A page size under 1 throws it with `api_page_size_invalid`.

### Limits and the page after the last page

`PaginationOptions` (optional last argument) sets the service's limits:

| Property | Default | Notes |
|---|---|---|
| `MaxPageSize` | `100` | A larger page size is lowered to it; `size` in the response shows the value used. |
| `PastLastPage` | `ReturnLastPage` | What a request for the page after the last page returns. `ReturnLastPage` returns the last page; the response's `page` field holds the last page number, not the requested one. `ReturnEmptyPage` returns an empty page; the `page` field keeps the requested number, `hasNextPage` is false, and `firstRowOnPage` and `lastRowOnPage` are 0. |

```csharp
private static readonly PaginationOptions LogPaging = new()
{
    MaxPageSize = 200,
    PastLastPage = PastLastPageBehavior.ReturnEmptyPage,
};
```

### Paging an async query

`PageWindow` works out which rows belong to the requested page. With it, an async query (EF Core,
Dapper, a MongoDB find) is paged by the same rules as `ToPagedResponse`:

```csharp
PageWindow.ValidateRequest(paging.Page, paging.Size);
var orders = dbContext.Orders.OrderByDescending(order => order.CreatedAt);
var window = PageWindow.Resolve(paging.Page, paging.Size, await orders.LongCountAsync(cancellationToken), options);
var pageRows = await orders.Skip(window.Skip).Take(window.Size).ToListAsync(cancellationToken);
return window.ToResponse(pageRows, page => page.Select(ToDto).ToList());
```

Pinqponq.Database.Mongo wraps this for a MongoDB find (`ToPagedResponseAsync`).

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
