# Pinqponq.Hosting.Cors

Sets up the ASP.NET Core CORS policy from a configured list of origins. The package has no default
origins, so a missing list stops the host at startup instead of silently denying or allowing every origin:

- the origin list comes from configuration or code,
- an empty list stops the host, and the message names the setting,
- an origin that has a path, a trailing slash or a scheme other than http or https stops the host,
- `*` together with credentials stops the host, because browsers reject that answer,
- an optional switch allows any origin in the Development environment when the list is empty.

## Install

```bash
dotnet add package Pinqponq.Hosting.Cors
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core

## Quick start

```csharp
builder.Services.AddPinqponqCors(builder.Configuration.GetSection("Cors"));

var app = builder.Build();
app.UseRouting();
app.UseCors();
app.UseAuthorization();
```

```json
{
  "Cors": {
    "AllowedOrigins": [ "https://app.example.com" ],
    "AllowCredentials": true
  }
}
```

Without a configuration section:

```csharp
builder.Services.AddPinqponqCors(options =>
{
    options.AllowedOrigins = ["https://app.example.com"];
});
```

`app.UseCors()` stays in your pipeline: put it after routing and before authorization.

## Options

| Option | Default | Meaning |
|---|---|---|
| `AllowedOrigins` | none | Origins that may call the API, such as `https://app.example.com` (scheme, host, optional port; no path, no trailing slash). `*` alone allows every origin. |
| `AllowCredentials` | `false` | Adds `Access-Control-Allow-Credentials`. Cannot be combined with an origin list that allows every origin. |
| `AllowedHeaders` | empty | Allowed request headers. Empty allows any header. |
| `AllowedMethods` | empty | Allowed HTTP methods. Empty allows any method. |
| `ExposedHeaders` | empty | Response headers the browser may read. |
| `PreflightMaxAge` | not set | How long a browser may cache a preflight answer. Must be greater than zero when set. |
| `PolicyName` | not set | When not set, the policy is the default policy and `app.UseCors()` applies it. When set, call `app.UseCors("<name>")`. |
| `AllowAnyOriginInDevelopmentWhenEmpty` | `false` | An empty list allows every origin in the Development environment. In any other environment an empty list still stops the host. |

## Startup validation

The options are validated when the host starts. A failure lists every problem, for example:

```
AllowedOrigins must contain at least one origin.
AllowedOrigins entry 'https://app.example.com/' must be an http or https origin without a path, such as 'https://app.example.com'.
AllowCredentials cannot be combined with an origin list that allows every origin.
```

## What stays in your application

The origin values, whether the browser needs credentials, and where `UseCors` sits in the pipeline.
