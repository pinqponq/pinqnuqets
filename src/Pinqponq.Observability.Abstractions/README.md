# Pinqponq.Observability.Abstractions

`ILogService`, the logging contract implemented by [Pinqponq.Observability](../Pinqponq.Observability/README.md),
with no dependencies. Reference it from domain and application projects that log, so they do not
take ASP.NET Core or the Pinqloq client with them; the API project references Pinqponq.Observability,
which registers the implementation (`LogService`).

## Install

```bash
dotnet add package Pinqponq.Observability.Abstractions
```

## Usage

```csharp
using Pinqponq.Observability;

public sealed class OrderExporter(ILogService logService)
{
    public void ReportFailure(int orderId, Exception exception) =>
        logService.Error("Export failed", exception,
            new Dictionary<string, object?> { ["orderId"] = orderId },
            eventName: "order_export_failed");
}
```

The type lives in the `Pinqponq.Observability` namespace, the same as the implementation.
`Info`, `Warn`, `Error` and `Debug` take the message, optional `data`
(`Dictionary<string, object?>`), an optional exception (`Warn`, `Error`), and the Pinqloq
collection, event name, device identifier, path and correlation id. What each field becomes is
described in the Pinqponq.Observability README.

## Repository

https://github.com/pinqponq/pinqnuqets
