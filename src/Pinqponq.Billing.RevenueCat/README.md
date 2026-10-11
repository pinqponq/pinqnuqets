# Pinqponq.Billing.RevenueCat

RevenueCat webhook handling for products with subscriptions: the payload model, the
Authorization header check and the event dispatch that turns webhook events into
subscription state. How a subscription is stored stays with the product — the package asks
for it through one interface, `IRevenueCatSubscriptionStore`.

## Install

```bash
dotnet add package Pinqponq.Billing.RevenueCat
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A webhook configured in the RevenueCat dashboard with an Authorization header value

## Quick start

Implement the store on top of your database:

```csharp
public sealed class SubscriptionStore(AppDbContext db) : IRevenueCatSubscriptionStore
{
    public bool TryResolveSubscriberId(string? appUserId, out string subscriberId)
    {
        subscriberId = int.TryParse(appUserId, out var id) ? id.ToString() : string.Empty;
        return subscriberId.Length > 0;
    }

    public Task<bool> HasProcessedEvent(string eventId, CancellationToken cancellationToken = default) =>
        db.ProcessedWebhookEvents.AnyAsync(entry => entry.EventId == eventId, cancellationToken);

    public void RecordProcessedEvent(string eventId, string eventType, DateTime processedAtUtc) =>
        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent(eventId, eventType, processedAtUtc));

    public async Task<RevenueCatSubscription?> FindSubscription(string subscriberId, CancellationToken cancellationToken = default) =>
        /* load your row and map it to RevenueCatSubscription */;

    public void SaveSubscription(RevenueCatSubscription subscription) =>
        /* add or update your row from the subscription */;

    public Task Commit(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
```

Register the package and expose the webhook:

```csharp
builder.Services.AddPinqponqRevenueCat<SubscriptionStore>(options =>
{
    options.WebhookAuthHeader = builder.Configuration["RevenueCat:WebhookAuthHeader"]!;
    options.PremiumEntitlementId = "premium";
});

app.MapPost("/webhooks/revenuecat", async (
    HttpRequest request,
    RevenueCatWebhookPayload payload,
    IRevenueCatWebhookAuthenticator authenticator,
    IRevenueCatEventHandler handler,
    CancellationToken cancellationToken) =>
{
    if (!authenticator.IsAuthorized(request.Headers.Authorization))
    {
        return Results.Unauthorized();
    }

    return await handler.Handle(payload, cancellationToken) ? Results.Ok() : Results.BadRequest();
}).AllowAnonymous();
```

## Configuration

`AddPinqponqRevenueCat<TStore>(Action<RevenueCatOptions> configure)` registers the handler
(scoped), the authenticator (singleton), your store (scoped) and validates the options on
startup.

| Option | Default | Notes |
|---|---|---|
| `WebhookAuthHeader` | none | The Authorization header value set in the RevenueCat dashboard. Required. |
| `PremiumEntitlementId` | none | The entitlement that counts as premium; must match the dashboard and the mobile app. Required. |

## Main types

- `IRevenueCatEventHandler` — `Handle(payload)` applies one event. Returns `false` only for an
  invalid payload (no event, id or type); a duplicate delivery returns `true` and changes nothing.
- `IRevenueCatWebhookAuthenticator` — constant-time check of the Authorization header. An
  unconfigured secret authorizes nothing.
- `IRevenueCatSubscriptionStore` — the product's storage. Everything saved between two
  `Commit` calls must be written together, so an event and its effects are stored atomically.
- `IRevenueCatWebhookObserver` — optional. Receives a structured record for each noteworthy
  handler step (named by `RevenueCatWebhookLogEvents`) next to the `ILogger` output, so a
  product can forward them to its own log pipeline. Register your own implementation before
  or after `AddPinqponqRevenueCat`; the default ignores them.
- `RevenueCatWebhookPayload`, `RevenueCatEvent`, `RevenueCatEventType` — the webhook body.

## Event behavior

| Event | Effect |
|---|---|
| `INITIAL_PURCHASE`, `RENEWAL`, `PRODUCT_CHANGE`, `UNCANCELLATION`, `NON_RENEWING_PURCHASE` | Creates or updates the subscription: active when the event carries the premium entitlement, renewing, product/store/expiry/transaction refreshed. |
| `CANCELLATION` | Stops renewal; premium stays on until the period ends. |
| `EXPIRATION` | Deactivates the subscription. |
| `BILLING_ISSUE` | Marks the grace period; premium stays on. |
| `TRANSFER` | Deactivates the subscribers in `transferred_from`, activates those in `transferred_to`. |
| `SUBSCRIBER_ALIAS`, `TEST` | Recorded as processed; no subscription change. |
| anything else | Observed as unhandled and recorded as processed. |

`CANCELLATION`, `EXPIRATION` and `BILLING_ISSUE` are skipped when they belong to an older
period than the stored one — a different `original_transaction_id`, or an expiry before the
stored one. RevenueCat sends such events right after a resubscribe, and applying them would
drop the fresh premium.

Subscribers are found from `app_user_id` first, then from the `aliases`, through
`IRevenueCatSubscriptionStore.TryResolveSubscriberId`. Events whose subscriber cannot be
resolved are observed and recorded without a subscription change.

## Notes

The package has no ASP.NET Core dependency: routing and the HTTP status codes stay in the
product, so the same handler serves minimal APIs and controllers.
