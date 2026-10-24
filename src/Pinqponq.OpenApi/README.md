# Pinqponq.OpenApi

ASP.NET Core OpenAPI and the Scalar reference set up in one call. The package publishes the
Bearer and API key security schemes, adds a security requirement only to the operations that need
one, sets the server URL from the request, and serves the document only in the environments you
allow.

## Install

```bash
dotnet add package Pinqponq.OpenApi
```

## Requirements

- .NET 10. The package uses `AddOpenApi` and Microsoft.OpenApi 2.x, which ship with .NET 10.
  .NET 9 uses Microsoft.OpenApi 1.x with different types, and .NET 8 has no built-in OpenAPI
  document generation, so neither is supported.
- ASP.NET Core. Microsoft.AspNetCore.OpenApi, Microsoft.OpenApi and Scalar.AspNetCore are installed with this package.

## Quick start

```csharp
builder.Services.AddPinqponqOpenApi(options =>
{
    options.Title = "Orders API";
    options.ApiKeyAuthentication.Enabled = true;
    options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
});

var app = builder.Build();
app.UseForwardedHeaders();   // first, so the server URL is the public one behind a proxy
app.UseAuthentication();
app.UseAuthorization();
app.MapPinqponqOpenApi();    // /openapi/v1.json and /scalar/v1
```

By default the document and the reference are served only in the Development environment. In any
other environment the routes answer `404`.

## Configuration

`AddPinqponqOpenApi(Action<PinqponqOpenApiOptions>?)` sets the options in code.
`AddPinqponqOpenApi(IConfiguration section, Action<PinqponqOpenApiOptions>? configure)` binds them
from a configuration section (for example `OpenApi`) and then runs `configure`.

```json
{
  "OpenApi": {
    "Title": "Orders API",
    "ExposeIn": "Always",
    "AuthorizationPolicy": "",
    "ApiKeyAuthentication": { "Enabled": true, "HeaderName": "X-Api-Key" }
  }
}
```

| `PinqponqOpenApiOptions` | Default | Notes |
|---|---|---|
| `Title` | none | Value of `info.title`. When not set, ASP.NET Core's title is kept. |
| `Description` | none | Value of `info.description`. |
| `BearerAuthentication` | see below | The HTTP Bearer scheme. |
| `ApiKeyAuthentication` | off | An API key scheme sent in a header. |
| `SecurityRequirementScope` | `AuthorizedOperations` | `AllOperations` adds a requirement to every operation. |
| `ServerUrlFromRequest` | `true` | `servers` is the scheme, host and path base of the request, without a trailing slash. When `false`, ASP.NET Core's own entry is kept, and it ends with a slash. |
| `SchemaIdNaming` | `Default` | `FullName` builds schema ids from the full type name, with `.` and `+` replaced by `_`. |
| `ExposeIn` | `DevelopmentOnly` | `Always` or `Never` serve the document in every or in no environment. |
| `AuthorizationPolicy` | none | Policy required to read the document and the reference. An empty string requires an authenticated user. |
| `OpenApiRoutePattern` | `/openapi/{documentName}.json` | Must contain `{documentName}`. The document is named `v1`. |
| `ScalarRoutePrefix` | `/scalar` | The reference is served at `<prefix>/v1`. |

| `BearerAuthentication` | Default | Notes |
|---|---|---|
| `Enabled` | `true` | |
| `SchemeName` | `Bearer` | Key in `components.securitySchemes`. |
| `AuthenticationScheme` | `Bearer` | The scheme is published only when this ASP.NET Core authentication scheme is registered. `null` publishes it without the check. |
| `BearerFormat` | `JWT` | |
| `Description` | `Bearer access token.` | |

| `ApiKeyAuthentication` | Default | Notes |
|---|---|---|
| `Enabled` | `false` | |
| `HeaderName` | none | Required when enabled. The package has no default header name. |
| `SchemeName` | `ApiKey` | Key in `components.securitySchemes`. |
| `Description` | none | |

Options are validated on startup.

## Security requirements

With the default scope, an operation gets a requirement only when its endpoint has authorization
metadata (`[Authorize]` or `RequireAuthorization()`) and no `[AllowAnonymous]`. An operation
protected only by a fallback authorization policy gets no requirement. Use `AllOperations` for that case.

When both schemes are published, each is its own requirement, so a caller needs only one of them.

## Behind a proxy

`ServerUrlFromRequest` reads the scheme and host of the request that asks for the document. Call
`app.UseForwardedHeaders()` before `MapPinqponqOpenApi()`, or the document lists the proxy's internal address.

## Serving in other environments

`ExposeIn = PinqponqOpenApiExposure.Always` serves the document in every environment. Combine it
with `AuthorizationPolicy` so anonymous callers cannot read the routes and wire models of a
production API. `MapPinqponqOpenApi()` returns a route group, so a product can add its own conventions to it.

## Changing the document

Implement `IPinqponqOpenApiCustomizer` and register it. Implement only the method you need.
Customizers run in registration order, after the package's own changes.

```csharp
internal sealed class TagCustomizer : IPinqponqOpenApiCustomizer
{
    public Task CustomizeOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Description ??= "See the integration guide.";
        return Task.CompletedTask;
    }
}

builder.Services.AddSingleton<IPinqponqOpenApiCustomizer, TagCustomizer>();
```
