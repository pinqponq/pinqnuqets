# Pinqponq.LiveKit.Server

A [LiveKit](https://livekit.io/) server API client for .NET: it issues the access
tokens your clients join rooms with, calls LiveKit's RoomService over its Twirp JSON
API, and verifies the webhooks LiveKit sends back. Requests are plain `HttpClient`
calls with `System.Text.Json` — no generated Protobuf/gRPC client and no Protobuf
toolchain.

## Install

```bash
dotnet add package Pinqponq.LiveKit.Server
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A LiveKit server — self-hosted or LiveKit Cloud — and its API key/secret pair

## Quick start

```csharp
using Pinqponq.LiveKit.Server;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPinqponqLiveKitServer(options => options.Url = "wss://my-project.livekit.cloud")
    .AddStaticCredentials(
        builder.Configuration["LiveKit:ApiKey"]!,
        builder.Configuration["LiveKit:ApiSecret"]!);

var app = builder.Build();

app.MapPost("/livekit/token", async (AccessTokenIssuer issuer, CancellationToken cancellationToken) =>
{
    var token = await issuer.CreateToken(
        new AccessTokenOptions
        {
            Identity = "user-123",
            Name = "Jane Doe",
            VideoGrant = new VideoGrant { RoomJoin = true, Room = "my-room" },
        },
        cancellationToken);

    return Results.Ok(new { token = token.Value, expiresAt = token.ExpiresAt });
});

app.Run();
```

Managing rooms and receiving webhooks from injected services:

```csharp
using Pinqponq.LiveKit.Server.Models;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Webhooks;

public sealed class MeetingService(RoomServiceClient roomServiceClient, WebhookReceiver webhookReceiver)
{
    public Task<IReadOnlyList<ParticipantInfo>> ListParticipantsAsync(string roomName, CancellationToken cancellationToken) =>
        roomServiceClient.ListParticipants(roomName, cancellationToken);

    public async Task HandleWebhookAsync(string rawBody, string? authorizationHeader, CancellationToken cancellationToken)
    {
        // Throws LiveKitWebhookValidationException unless LiveKit signed exactly this body.
        var webhookEvent = await webhookReceiver.Receive(rawBody, authorizationHeader, cancellationToken);

        if (webhookEvent.EventType == WebhookEventType.ParticipantJoined)
        {
            // webhookEvent.Room, webhookEvent.Participant, ...
        }
    }
}
```

## Configuration

`AddPinqponqLiveKitServer(Action<LiveKitServerOptions> configureOptions)` registers
`RoomServiceClient` (as a typed `HttpClient`), `AccessTokenIssuer` and
`WebhookReceiver`, and returns a `LiveKitServerBuilder` for the credentials:

| Option | Default | Notes |
|---|---|---|
| `Url` | `""` (required) | LiveKit server address. Accepts `http`, `https`, `ws` and `wss`, with an optional sub path; `ws`/`wss` are sent as `http`/`https`. |

Options are validated on startup via `ValidateOnStart()`, so a missing or
non-absolute `Url` fails fast instead of at the first RoomService call.

Credentials are registered on the returned builder — exactly one of:

- `AddStaticCredentials(apiKey, apiSecret)` — a fixed key/secret pair.
- `AddCredentialsProvider<TCredentialsProvider>(lifetime)` — your own
  `ILiveKitCredentialsProvider`, e.g. one that reads a secret store (default
  lifetime: `Singleton`).

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqLiveKitServer(Action<LiveKitServerOptions>)` | DI extension (`IServiceCollection`) | Registers the three services below and returns a `LiveKitServerBuilder`. |
| `LiveKitServerBuilder` | Builder | `AddStaticCredentials(...)` / `AddCredentialsProvider<T>(...)` register the `ILiveKitCredentialsProvider`. |
| `AccessTokenIssuer` | Service | `CreateToken(AccessTokenOptions, cancellationToken)` → `LiveKitAccessToken` (`Value`, `ExpiresAt`). |
| `AccessTokenOptions` | Options | `Identity`, `Name`, `Metadata`, `Attributes`, `VideoGrant`, `Ttl` (default `AccessTokenOptions.DefaultTtl`, 6 hours). |
| `VideoGrant` | Model | The token's `video` claim: room join/create/list/admin, publish/subscribe permissions, `CanPublishSources`, and the rest of LiveKit's grant fields. |
| `RoomServiceClient` | Service | `ListRooms`, `GetRoom`, `ListParticipants`, `RemoveParticipant`, `DeleteRoom` against LiveKit's RoomService. |
| `WebhookReceiver` | Service | `Receive(rawBody, authorizationHeader, cancellationToken)` → `WebhookEvent`, after verifying LiveKit's signature. |
| `WebhookEvent` | Model | `Id`, `Event` / `EventType`, `CreatedAt`, `Room`, `Participant`, `Track`. |
| `ILiveKitCredentialsProvider` | Interface | `GetCredentials(cancellationToken)` → `LiveKitCredentials`. Implement it to source the key/secret yourself. |
| `LiveKitApiException` | Exception | A RoomService call returned a non-success status: `StatusCode`, `ErrorCode` (Twirp code), `IsNotFound`. |
| `LiveKitWebhookValidationException` | Exception | A webhook is not authentic — see [Notes / behavior](#notes--behavior). |

## Notes / behavior

- **Each RoomService call carries its own least-privilege token.** `RoomServiceClient`
  signs a 10-minute token per call holding only the grant that call needs —
  `roomList` for `ListRooms` / `GetRoom`, `roomAdmin` limited to the target room for
  `ListParticipants` / `RemoveParticipant`, and `roomCreate` for `DeleteRoom`.
- **Token options are checked before signing.** `CreateToken` throws
  `ArgumentOutOfRangeException` for a non-positive `Ttl`, and `ArgumentException` when
  a `RoomJoin` grant has no `Identity`, or a `RoomJoin` / `RoomAdmin` grant has no
  `Room`. Tokens are HS256-signed with the API secret.
- **Unset publish/subscribe permissions mean "granted".** On `VideoGrant`,
  `CanPublish`, `CanSubscribe` and `CanPublishData` are nullable on purpose: LiveKit
  treats an unset permission as granted and only an explicit `false` denies it. Do
  not default them to `false` when building a grant.
- **Webhook verification fails closed.** `Receive` throws
  `LiveKitWebhookValidationException` when the `Authorization` header is missing,
  the token's signature, issuer (your API key) or lifetime is invalid, or the body's
  SHA-256 does not match the checksum in the token. Pass the request body exactly as
  received — the checksum covers those bytes, so a re-serialized body will not verify.
- **Webhooks can arrive more than once.** LiveKit retries failed deliveries; use
  `WebhookEvent.Id` to discard duplicates. Egress, ingress and agent job payloads are
  not modeled — read them from the raw body if you need them.
- **API errors keep LiveKit's code.** `LiveKitApiException.ErrorCode` holds the Twirp
  error code (`not_found`, `permission_denied`, …) and is `null` when the response was
  not a Twirp error at all, e.g. an HTML page from a proxy. `GetRoom` returns `null`
  for a room that isn't open rather than throwing.
- **Credentials are read on every call.** The `ILiveKitCredentialsProvider` is called
  each time a token is signed or a webhook is verified, so a provider backed by a
  remote secret store should cache internally. `LiveKitCredentials.ToString()` omits
  the secret, so it is safe to log.

## Related packages

- [`Pinqponq.Identity`](../Pinqponq.Identity) — authenticates your own users; issue a
  LiveKit access token only after that check passes.
- [`Pinqponq.ErrorHandling`](../Pinqponq.ErrorHandling) — map
  `LiveKitWebhookValidationException` and `LiveKitApiException` to standard error
  responses through its `StatusMappingResolver`.

## Samples

Try this package in the browser via [Pinqponq.Playground](../../samples/Pinqponq.Playground) —
see [samples/README.md](../../samples/README.md).

## Repository

https://github.com/pinqponq/pinqnuqets
