# Pinqponq.Hosting.Maintenance

Closes the API for maintenance with one configuration switch. While the switch is on, every request is
answered with the standard error body and the status code you set (503 by default), and nothing after the
middleware runs:

- the switch is read on every request, so a configuration reload turns maintenance mode on or off without a restart,
- the error body comes from `Pinqponq.ErrorHandling`, so it has the same shape as every other error and is localized when error localization is on (unless you turn that off with `LocalizeMessage`),
- paths such as health checks and webhooks can stay open,
- a `Retry-After` header is sent when you set one,
- every blocked request is logged as a warning with its method and path.

## Install

```bash
dotnet add package Pinqponq.Hosting.Maintenance
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core

## Quick start

```csharp
builder.Services.AddPinqponqMaintenanceMode(builder.Configuration);   // binds "MaintenanceMode"

var app = builder.Build();
app.UsePinqponqMaintenanceMode();                                     // place before routing and authentication
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
```

```json
{
  "MaintenanceMode": {
    "Enabled": true,
    "ResponseCode": "planned_maintenance",
    "Message": "We are updating the service. Please try again in a few minutes.",
    "RetryAfterSeconds": 300,
    "ExemptPathPrefixes": [ "/webhooks" ]
  }
}
```

An optional callback runs after binding, for settings you want to set in code:

```csharp
builder.Services.AddPinqponqMaintenanceMode(builder.Configuration, options =>
{
    options.ExemptPathPrefixes.Add("/status");
});
```

`app.UsePinqponqMaintenanceMode()` stays in your pipeline. Put it before routing, authentication and anything
else that should not run during maintenance. When you use `UsePinqponqRequestLogging` from
`Pinqponq.Observability`, put it after that call, so the blocked requests are logged as requests too.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Turns maintenance mode on. Read through `IOptionsMonitor`, so a configuration reload takes effect on the next request. |
| `Message` | `"The service is temporarily unavailable because of maintenance."` | Message of the error body. With error localization on and `LocalizeMessage` on, the catalog message for `ResponseCode` is written instead. A `null` value from configuration keeps the default. |
| `LocalizeMessage` | `true` | Set it to `false` to always write `Message`, also when error localization is on. |
| `ResponseCode` | none, required | Response code of the error body, such as `planned_maintenance`. Each application sets its own code. |
| `EventName` | `"request_blocked_by_maintenance"` | Log event name of a blocked request on `IRequestErrorFeature`. |
| `StatusCode` | `503` | HTTP status code of a blocked request, from 400 to 599. |
| `RetryAfterSeconds` | not set | When set, a blocked request gets a `Retry-After` header with this number of seconds. Must be greater than zero. |
| `ExemptPathPrefixes` | `["/health"]` | Path prefixes that keep working during maintenance. See below. |

### Exempt paths

A prefix matches whole path segments without regard to case: `/health` matches `/health` and `/health/ready`,
but not `/healthz`. Each entry starts with `/`.

Entries bound from configuration are added to the default `/health` entry. To remove the default entry, clear
the list in the callback:

```csharp
builder.Services.AddPinqponqMaintenanceMode(builder.Configuration, options =>
{
    options.ExemptPathPrefixes.Clear();
    options.ExemptPathPrefixes.Add("/ping");
});
```

## Localized messages

When `AddPinqponqErrorLocalization` from `Pinqponq.ErrorHandling` is on, the message comes from the error
catalog for `ResponseCode`, in the language of the request. Set `LocalizeMessage` to `false` to write the
configured `Message` instead. The package ships no catalog entry for a maintenance
code, because each application chooses its own code. Add your code to your application's catalog files
(`Localization/EN.json`, `Localization/TR.json`, …). A code the catalog does not know gets the message of the
catalog's `FallbackResponseCode`.

## Request logs

A blocked request is described on `IRequestErrorFeature` under `EventName`, by default
`request_blocked_by_maintenance` (`MaintenanceModeEvents.RequestBlockedByMaintenance`), so
`Pinqponq.Observability` writes it into the request's log record. Set `EventName` when your logs already use
another name for this event.

## Startup validation

The options are validated when the host starts and again when the configuration is reloaded. A failure lists
every problem:

- `StatusCode` outside 400 to 599,
- a missing or blank `ResponseCode`,
- a blank `EventName`,
- a `RetryAfterSeconds` of zero or less,
- an `ExemptPathPrefixes` entry that does not start with `/`.

An invalid reload makes every request fail until the configuration is valid again, because the options cannot
be read. This includes the exempt paths, such as `/health`.
