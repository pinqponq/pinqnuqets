# Pinqponq.Push.Fcm

Firebase Cloud Messaging (FCM) sender for backends that push to mobile apps. You give it a
Firebase service account; it gives you an `IFcmSender` that sends notifications and data-only
pushes to device tokens, sends to topics and manages topic subscriptions. Every send to tokens
returns one result per token that says whether the token is invalid (delete it) or the push
provider credentials are broken (fix them, every push to that platform fails).

The client side is in [deveng-core-kmp](https://github.com/pinqponq/deveng-core-kmp)
(pinqponq/deveng-core-kmp#116): it registers the device with FCM and hands the token to your
backend.

## Install

```bash
dotnet add package Pinqponq.Push.Fcm
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A Firebase service account JSON (Firebase console → Project settings → Service accounts)
- An `ILogService` registration, for example from `Pinqponq.Observability`

## Quick start

```json
{
  "Fcm": {
    "CredentialPath": "firebase-service-account.json"
  }
}
```

```csharp
builder.Services.AddPinqponqObservability(/* … */);
builder.Services.AddPinqponqFcm(builder.Configuration.GetSection(FcmOptions.SectionName));
```

```csharp
public sealed class ReminderNotifier(IFcmSender fcmSender)
{
    public Task<IReadOnlyList<FcmSendResult>> Notify(IReadOnlyCollection<string> deviceTokens) =>
        fcmSender.SendToTokens(
            new FcmMessage { Title = "Reminder", Body = "Your session starts in 10 minutes" },
            deviceTokens);
}
```

The service account JSON is a secret: keep the file out of the repository, or read the JSON
from Vault (`Pinqponq.Configuration.Vault`) into `Fcm:CredentialJson`.

## Options

| Option | Default | Meaning |
|---|---|---|
| `IsEnabled` | `true` | `false` sends nothing and needs no credential (developer machines). |
| `CredentialPath` | — | Service account JSON file. A relative path is resolved against the app directory. |
| `CredentialJson` | — | The service account JSON itself. Set this or `CredentialPath`, not both. |
| `DataOnlyAndroidPriority` | `Normal` | Android priority of `SendDataOnlyToTokens`. `High` wakes the device at once; use it only when the app shows a notification for every such push, or Android lowers the app's priority. |

The options are validated at startup: when `IsEnabled` is `true`, exactly one credential must
be set, the file must exist and the JSON must be a service account.

## Sending

| Method | Sends |
|---|---|
| `SendToTokens(message, tokens)` | A notification (title, body, optional image and data) to device tokens. |
| `SendDataOnlyToTokens(data, tokens)` | Data without a notification block; the app decides what to show. |
| `SendToTopic(topic, message)` | A notification to every device subscribed to the topic. |
| `SubscribeToTopic(tokens, topic)` / `UnsubscribeFromTopic(tokens, topic)` | Topic membership. Rejected tokens are logged. |

- More than 500 tokens are split into several FCM calls (1,000 for topic changes); you can pass any number.
- No tokens, or `IsEnabled` set to `false`, sends nothing and returns an empty list.
- `FcmMessage.Android` and `FcmMessage.Apns` take FirebaseAdmin's `AndroidConfig` and `ApnsConfig`
  as they are. The package sets nothing there by default: no sound, badge or content-available.

## Results and invalid tokens

`SendToTokens` and `SendDataOnlyToTokens` return an `FcmSendResult` per token, in the order the
tokens were given:

| Field | Meaning |
|---|---|
| `IsSuccess` | FCM accepted the push. Acceptance is not proof of delivery. |
| `ErrorCode` / `ErrorMessage` | FCM's error code (`Unregistered`, `ThirdPartyAuthError`, …), or the general Firebase code when the request failed below the messaging layer. |
| `IsTokenInvalid` | FCM answered `Unregistered`: the token will never work again. |
| `IsProviderCredentialInvalid` | FCM answered `ThirdPartyAuthError`: the APNs key (or another provider credential) is invalid. Not the token's fault. |

`InvalidArgument` and `SenderIdMismatch` are not treated as invalid tokens: a bad payload or the
wrong Firebase project gives the same answer for every token, and deleting them all would be
wrong. They come back as failures with their `ErrorCode`.

To delete invalid tokens, implement `IFcmInvalidTokenHandler` and register it; the sender calls it
after each send with the tokens FCM reported as `Unregistered`:

```csharp
public sealed class DeviceTokenCleaner(AppDbContext dbContext) : IFcmInvalidTokenHandler
{
    public Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default) =>
        dbContext.Devices.Where(device => invalidTokens.Contains(device.PushToken)).ExecuteDeleteAsync(cancellationToken);
}

builder.Services.AddScoped<IFcmInvalidTokenHandler, DeviceTokenCleaner>();
```

## Logging

Failures are written through `ILogService` with the event names in `FcmLogEvents`. Tokens are
credentials, so a record holds only the last 6 characters of each token.

| Event | Level | When |
|---|---|---|
| `fcm_send_partial_failure` | Warning | Some tokens of a send failed. |
| `fcm_provider_credential_invalid` | Error | A send failed with `ThirdPartyAuthError`. |
| `fcm_topic_subscribe_partial_failure` / `fcm_topic_unsubscribe_partial_failure` | Warning | FCM rejected tokens of a topic change; each failure has FCM's reason. |
| `fcm_disabled` | Debug | `IsEnabled` is `false` and a send was skipped. |

## Firebase app

The package creates its own named Firebase app (`Pinqponq.Push.Fcm`) on the first send and
reuses it afterwards. A default Firebase app your application creates for something else is
left alone.

## What stays in your application

Device token storage, notification texts and their translations, topic names and segments,
user preferences, scheduling, and platform-specific pushes such as APNs widget updates.

## Repository

https://github.com/pinqponq/pinqnuqets
