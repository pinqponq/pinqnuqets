# Pinqponq.Messaging.RabbitMq

A RabbitMQ transport layer for .NET built on the v7 async client API:
publishing with publisher confirms and mandatory delivery
(`IMessagePublisher`), a hosted background consumer with automatic topology
declaration and dead-lettering (`AddRabbitMqConsumer<THandler>`), and shared
connection/channel management with an explicit reconnect strategy. It carries
no message contracts or serialization opinions — you decide what a message
body looks like.

## Install

```bash
dotnet add package Pinqponq.Messaging.RabbitMq
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A reachable RabbitMQ broker
- `RabbitMQ.Client` v7 (pulled in transitively)

## Quick start

Publishing:

```csharp
using Pinqponq.Messaging.RabbitMq;
using Pinqponq.Messaging.RabbitMq.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqRabbitMq(broker =>
{
    broker.HostName = builder.Configuration["RabbitMq:Host"]!;
    broker.Port = 5672;
    broker.UserName = builder.Configuration["RabbitMq:User"]!;
    broker.Password = builder.Configuration["RabbitMq:Password"]!;
    broker.VirtualHost = "/";
    broker.PrefetchCount = 20;
});

var app = builder.Build();

app.MapPost("/orders/{id}/submit", async (string id, IMessagePublisher publisher, CancellationToken ct) =>
{
    await publisher.PublishAsync(exchange: string.Empty, routingKey: "orders.submitted", message: id, ct);
    return Results.Accepted();
});

app.Run();
```

Consuming, with a handler and a dedicated queue:

```csharp
public sealed class OrderSubmittedHandler(ILogger<OrderSubmittedHandler> logger) : IMessageHandler
{
    public Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing order {OrderId}", message);
        return Task.CompletedTask;
    }
}
```

```csharp
builder.Services.AddRabbitMqConsumer<OrderSubmittedHandler>(consumer =>
{
    consumer.Queue = "orders.submitted";
    // EnableDeadLetter defaults to true — failed messages go to "orders.submitted.dead".
});
```

## Configuration

### `RabbitMqOptions` — connection, passed to `AddPinqponqRabbitMq`

| Property | Type | Default | Notes |
|---|---|---|---|
| `HostName` | `string` | `"localhost"` (required) | Broker host name. |
| `Port` | `int` | `5672` | Broker port; must be between 1 and 65535. Use `5671` when `UseSsl` is enabled (AMQPS). |
| `UserName` | `string` | `"guest"` | Broker user name. |
| `Password` | `string` | `"guest"` | Broker password. |
| `VirtualHost` | `string` | `"/"` | Virtual host. |
| `PrefetchCount` | `ushort` | `10` | Consumer QoS prefetch (`BasicQos`, non-global). Must be at least 1. |
| `PublishRetryCount` | `int` | `3` | Max retry attempts when publishing on transient broker faults. Must not be negative; `0` disables retry entirely (no Polly pipeline is built). |
| `PublishRetryBaseDelay` | `TimeSpan` | `200ms` | Base delay for exponential backoff between publish retries. Must be positive. |
| `UseSsl` | `bool` | `false` | Enables TLS (AMQPS) on the connection. Typical TLS port is `5671`. |
| `SslServerName` | `string?` | `null` | TLS server name used for certificate validation; falls back to `HostName` when empty. |

### `RabbitMqConsumerOptions` — per-consumer topology, passed to `AddRabbitMqConsumer<THandler>`

| Property | Type | Default | Notes |
|---|---|---|---|
| `Queue` | `string` | *(required)* | The queue to declare and consume from. Validated **at registration time**: an empty value throws `InvalidOperationException` immediately, before any broker connection is attempted. |
| `Exchange` | `string` | `""` | Exchange to bind the queue to. Empty uses the default (nameless) exchange, i.e. publish directly to the queue name as the routing key. |
| `RoutingKey` | `string?` | `null` | Routing key for the binding. Defaults to `Queue` when unset. |
| `EnableDeadLetter` | `bool` | `true` | Declares a dead-letter exchange/queue and routes failed messages there instead of requeueing. |
| `DeadLetterExchange` | `string?` | `null` | Dead-letter exchange name. Defaults to `{Queue}.dlx`. |
| `DeadLetterQueue` | `string?` | `null` | Dead-letter queue name. Defaults to `{Queue}.dead`. |
| `MaxRedeliveryCount` | `int` | `5` | Used **only** when `EnableDeadLetter` is `false`: maximum republish attempts for a failing message before it's dropped via `nack(requeue:false)`. Must be at least 1 — the consumer throws `ArgumentOutOfRangeException` at construction otherwise. |

`RabbitMqOptions` is validated at startup via `ValidateOnStart()`.
`RabbitMqConsumerOptions` is validated synchronously inside
`AddRabbitMqConsumer<THandler>` itself (queue name) and inside the consumer's
constructor (`MaxRedeliveryCount`) — both fail before the host starts, not on
the first message.

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqRabbitMq(Action<RabbitMqOptions>)` | DI extension (`IServiceCollection`) | Registers the shared `IRabbitMqConnection` and `IMessagePublisher`. |
| `AddRabbitMqConsumer<THandler>(Action<RabbitMqConsumerOptions>)` | DI extension (`IServiceCollection`) | Registers `THandler` as scoped and a background `IHostedService` that declares topology, consumes, and dispatches to a new `THandler` per message inside its own DI scope. |
| `IMessagePublisher` | Interface | `PublishAsync(exchange, routingKey, body\|message, ct)` — publishes with publisher confirms and `mandatory: true`; persistent messages. |
| `IMessageHandler` | Interface | `HandleAsync(message, cancellationToken)`. Implement one per message type/queue; throwing rejects the message. |
| `IRabbitMqConnection` | Interface (`IAsyncDisposable`) | `CreateChannelAsync([options], ct)` — hands out channels off a single lazily-created, explicitly-reconnected connection. |
| `RabbitMqOptions` | Options | Broker connection, QoS, publish retry, TLS. |
| `RabbitMqConsumerOptions` | Options | Per-consumer queue/exchange/dead-letter topology. |
| `RabbitMqPublisher`, `RabbitMqConnection`, `RabbitMqConsumerService<THandler>` | Implementations | Registered by the extension methods; not usually referenced directly. |

## Notes / behavior

- **Publisher confirms + mandatory, always.** Every publish opens a channel
  with `publisherConfirmationsEnabled: true` and
  `publisherConfirmationTrackingEnabled: true`, and calls `BasicPublishAsync`
  with `mandatory: true`. **An unroutable message throws** (RabbitMQ.Client
  surfaces this as a `PublishException` when the broker returns a `basic.return`
  for a mandatory-but-unroutable publish) instead of being silently dropped —
  make sure the target exchange/routing key actually resolves to a queue.
  Messages are marked `Persistent = true`.
- **Publish retry is opt-in via count.** When `PublishRetryCount > 0`, a Polly
  pipeline retries `BrokerUnreachableException`, `AlreadyClosedException`,
  `OperationInterruptedException`, `PublishException`, `IOException`, and
  `TimeoutException` with exponential backoff and jitter. With
  `PublishRetryCount == 0`, no pipeline is built at all and a failed publish
  propagates on the first attempt.
- **Dead-lettering is on by default.** `RabbitMqConsumerOptions.EnableDeadLetter`
  defaults to `true`: on handler failure the consumer `nack`s
  `requeue: false` and the broker's own DLX routing (declared by this
  package as a `fanout` exchange bound to the dead-letter queue) moves the
  message to `{Queue}.dead`. **When `EnableDeadLetter` is `false`**, the
  package falls back to its own redelivery counter: it stamps an
  `x-pinqponq-redelivery` header, republishes to the same queue, and after
  `MaxRedeliveryCount` attempts drops the message via
  `nack(requeue: false)` rather than looping forever.
- **One handler instance per message, in its own scope.** The consumer
  creates a new DI scope (`IServiceScopeFactory.CreateAsyncScope`) and
  resolves a fresh `THandler` for every delivery, so handlers can safely take
  scoped dependencies (e.g. a `DbContext`).
- **Explicit reconnect, not RabbitMQ.Client's automatic recovery.**
  `RabbitMqConnection` sets `AutomaticRecoveryEnabled = false` on the
  underlying `ConnectionFactory` and instead lazily (re)creates a single
  connection under a semaphore whenever the cached one isn't `IsOpen`. The
  consumer's own loop additionally detects a channel shutdown, tears down,
  waits `3s`, and starts a fresh session — so publishers and the consumer
  each drive their own reconnect rather than relying on client-side
  auto-recovery event ordering.
- **Graceful shutdown drains in-flight work.** On `StopAsync`, the consumer
  cancels its broker subscription and waits up to `10s` for in-flight
  handler tasks to finish; anything still running past that timeout is
  `nack`ed with `requeue: true` so it's redelivered rather than lost.
- **TLS is opt-in.** Set `UseSsl = true` (and typically `Port = 5671`) to
  connect over AMQPS; `SslServerName` overrides the certificate validation
  name when it differs from `HostName`.
- **No message contracts.** This package works entirely with
  `string` / `ReadOnlyMemory<byte>` payloads. Message schemas, versioning,
  and (de)serialization are the consuming application's responsibility.

## Related packages

- [Pinqponq.Cache](../Pinqponq.Cache/README.md)
- [Pinqponq.Database.Postgres](../Pinqponq.Database.Postgres/README.md)
- [Pinqponq.Database.Mongo](../Pinqponq.Database.Mongo/README.md)
- [Pinqponq.Database.Mssql](../Pinqponq.Database.Mssql/README.md)

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
