# Pinqponq.Auth.ApiKey

A shared-secret check for service-to-service endpoints. One service calls another with a key in
a request header; the `[RequireApiKey]` action filter lets the request through only when the
header carries the configured key, and returns 401 otherwise.

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
