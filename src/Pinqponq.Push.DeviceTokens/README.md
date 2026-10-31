# Pinqponq.Push.DeviceTokens

The store of device tokens that clients register for push notifications, on EF Core. It keeps one row per
device token, moves a token to the user who registered it last, replaces the token of a known device when the
push provider rotates it, and removes tokens the provider rejected. Pair it with
[Pinqponq.Push.Fcm](../Pinqponq.Push.Fcm/README.md), which sends the pushes.

## Install

```bash
dotnet add package Pinqponq.Push.DeviceTokens
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- An EF Core `DbContext` on a relational provider (the package runs `ExecuteDelete`)

## Quick start

Add the table to your context and create a migration:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyPinqponqDeviceTokens();
}
```

Register the store:

```csharp
builder.Services.AddPinqponqDeviceTokens<AppDbContext>(options =>
{
    options.MaxTokenLength = 4096;
});
```

Use it:

```csharp
public sealed class DeviceService(IDeviceTokenRegistry registry)
{
    public Task Register(string userId, string token) =>
        registry.Register(userId, token, platform: "android", deviceIdentifier: "device-123");
}
```

`IDeviceTokenRegistry` has `Register`, `Unregister`, `GetTokens` and `RemoveTokens`.

## Rules the store applies

- A token is unique. A token that another user registered moves to the latest user, because a token addresses one
  device.
- With a `deviceIdentifier`, the user's row for that device takes the new token, so a rotated token does not stay
  behind. If the new token already sits on another row, that row is removed first.
- A row stored without an identifier adopts the identifier the client sends later.
- A client that sends no identifier matches on the token alone.
- Two requests that register the same new token at once end with one row.
- The identifier is matched per user. A user cannot take over another user's device row by sending its identifier.

## Dead tokens

`AddPinqponqDeviceTokens` registers an `IFcmInvalidTokenHandler` that calls `RemoveTokens`, so tokens that
`IFcmSender` reports as invalid leave the store without extra code. Set `RemoveTokensFcmReportsInvalid` to
`false`, or register your own handler after the call, to do something else. The call replaces an
`IFcmInvalidTokenHandler` registered before it: `IFcmSender` takes one handler, so register yours after.

## After a registration

Register an `IDeviceTokenRegisteredHandler` for what differs per product, for example subscribing the token to a
topic or forwarding it to a chat service. It runs after the row is saved and receives a
`DeviceTokenRegistration`: the row, whether it is new, and the previous user and token (`IsTokenRotation`).

```csharp
public sealed class TopicSubscriber(IFcmSender fcm) : IDeviceTokenRegisteredHandler
{
    public async Task Handle(DeviceTokenRegistration registration, CancellationToken cancellationToken = default)
    {
        await fcm.SubscribeToTopic("all-users", [registration.DeviceToken.Token], cancellationToken);
    }
}

builder.Services.AddScoped<IDeviceTokenRegisteredHandler, TopicSubscriber>();
```

An exception from a handler reaches the caller of `Register`; the row stays saved. A service that keeps its tokens
in another service (for example one that deactivates tokens over HTTP) does not use the store; it can implement
`IFcmInvalidTokenHandler` itself.

## Endpoints (optional)

```csharp
app.MapPinqponqDeviceTokenEndpoints();
```

`POST /device-tokens` registers (`{ "token", "platform", "deviceIdentifier" }`) and `DELETE /device-tokens`
unregisters (`{ "token" }`) for the signed-in user. Both return 204. An invalid request gets 400, a request
without a user id claim gets 401. The group requires authorization; the returned `RouteGroupBuilder` lets you add
a policy. A product that wants other routes or bodies writes its own endpoints over `IDeviceTokenRegistry`.

## Options

| Option | Default | Meaning |
|---|---|---|
| `MaxTokenLength` | `4096` | Longer tokens are rejected with `ArgumentException`. |
| `MaxDeviceIdentifierLength` | `256` | Longer identifiers are rejected. |
| `Platforms` | `android`, `ios`, `web` | Accepted platform names, compared without regard to case. |
| `RemoveTokensFcmReportsInvalid` | `true` | Registers the handler that removes tokens FCM rejected. |

`ApplyPinqponqDeviceTokens(options => …)` takes `TableName` (default `DeviceTokens`) and `Schema`, so an existing
table can stay. User ids are strings; map your own id type to a string at the call. The table has the columns
`Id`, `UserId`, `Token`, `Platform`, `DeviceIdentifier`, `CreatedAt` and `UpdatedAt`.

## Tests

The tests run against PostgreSQL in a Testcontainers container, so Docker has to be running.
