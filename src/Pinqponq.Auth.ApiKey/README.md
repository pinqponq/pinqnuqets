# Pinqponq.Auth.ApiKey

A shared-secret check for service-to-service endpoints. One service calls another with a key in
a request header; the `[RequireApiKey]` action filter lets the request through only when the
header carries the configured key, and returns 401 otherwise. The package also registers the
calling side: an `HttpClient` that sends the key.

## Install

```bash
dotnet add package Pinqponq.Auth.ApiKey
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core MVC controllers (the filter is an `IAsyncActionFilter`)

## Quick start

Register one key per endpoint group. The name connects the registration to the attribute:

```csharp
builder.Services.AddPinqponqApiKey("internal", options =>
{
    options.HeaderName = "X-Internal-Key";
    options.ApiKey = builder.Configuration["Internal:ApiKey"];
});
```

Guard a controller or an action with the same name:

```csharp
[ApiController]
[Route("internal/previews")]
[RequireApiKey("internal")]
public sealed class PreviewController : ControllerBase
{
    // ...
}
```

Call `AddPinqponqApiKey` again with another name to guard a second endpoint group with its own
key and header. A key registered under one name is never accepted for another name.

## Configuration

`AddPinqponqApiKey(string name, Action<ApiKeyOptions> configure)` registers the named
`ApiKeyOptions` and validates them on startup.

| Option | Default | Notes |
|---|---|---|
| `HeaderName` | none | The request header that carries the key. Required: startup fails when it is empty. |
| `ApiKey` | none | The shared secret. Keep it in Vault, not in `appsettings*.json`. |

The package has no default header name or key: each product chooses its own header name and
keeps the key in its own configuration.

## Behavior

| Request | Result |
|---|---|
| Header carries the configured key | The action runs. |
| Header missing, empty or different | 401 |
| No key configured for the name (empty, or the name was never registered) | 401, and a warning is logged |

The key is compared in constant time: both values are hashed with SHA-256 and the digests are
compared with `CryptographicOperations.FixedTimeEquals`, so the time taken does not depend on
how many characters match or on the length of the sent key. The comparison is case-sensitive.

## Calling a guarded service

`AddInternalApiKeyClient` registers an `HttpClient` that already has the other service's base
address, timeout and key header. Both sides use the same header name and key.

Receiver (the service that owns the endpoints):

```csharp
builder.Services.AddPinqponqApiKey("internal", options =>
{
    options.HeaderName = "X-Internal-Key";
    options.ApiKey = builder.Configuration["Internal:ApiKey"];
});
```

Sender (the service that calls them):

```csharp
builder.Services.AddInternalApiKeyClient<IUsersClient, HttpUsersClient>(
    builder.Configuration, sectionName: "UsersClient");
```

```json
{
  "UsersClient": {
    "BaseUrl": "https://users.internal",
    "HeaderName": "X-Internal-Key",
    "TimeoutSeconds": 5
  }
}
```

Keep `ApiKey` out of `appsettings*.json`: set `UsersClient:ApiKey` from Vault or an environment
variable.

| Option | Default | Notes |
|---|---|---|
| `BaseUrl` | none | Absolute http or https address of the other service. A trailing slash is added when it is missing. |
| `ApiKey` | none | The shared secret. Never logged. |
| `HeaderName` | none | The request header that carries the key. |
| `TimeoutSeconds` | `5` | Must be positive. |

Startup fails when the section is missing or a value is invalid; the message names the section
and the property. Every problem is listed in one message.

- Several typed clients can share one section: call `AddInternalApiKeyClient` for each with the
  same `sectionName`.
- `AddInternalApiKeyClient(name, configuration, sectionName)` registers a named client for
  `IHttpClientFactory.CreateClient(name)`.
- Both overloads return the `IHttpClientBuilder`, so a product can add its own handlers:
  `.AddHttpMessageHandler<MyHandler>()`.

The package has no default header name, section name or key. Section names, header names, client
interfaces and implementations stay in each product.
