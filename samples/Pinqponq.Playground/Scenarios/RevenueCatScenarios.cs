using Pinqponq.Billing.RevenueCat;
using Pinqponq.Billing.RevenueCat.DependencyInjection;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.Billing.RevenueCat</c>. The package never touches a network or a
/// database — storage is the in-memory store above — so every scenario runs offline.
/// </summary>
public static class RevenueCatScenarios
{
    private const string Package = "Pinqponq.Billing.RevenueCat";
    private const string DefaultSecret = "Bearer playground-revenuecat-secret";
    private const string Entitlement = "premium";

    private static readonly ScenarioField SecretField =
        new("secret", "Webhook Authorization header", ScenarioFieldKind.Password, DefaultSecret);

    public static IEnumerable<Scenario> Create()
    {
        yield return PurchaseThenCancel();
        yield return ResubscribeKeepsPremium();
        yield return RetriedDelivery();
        yield return RefuseForgedWebhook();
    }

    private static Scenario PurchaseThenCancel() => new(
        new ScenarioDescriptor
        {
            Id = "revenuecat.webhook.purchase-cancel",
            PackageId = Package,
            Title = "Purchase, then cancel",
            Summary = "A signed INITIAL_PURCHASE activates the subscriber; a CANCELLATION afterwards only "
                      + "stops renewal — premium stays on until the period ends.",
            Fields =
            [
                new ScenarioField("userId", "app_user_id", ScenarioFieldKind.Number, "21"),
                SecretField,
            ],
        },
        async context =>
        {
            var userId = context.Input.Text("userId");
            var secret = context.Input.Text("secret");
            var store = new InMemoryRevenueCatStore();
            await using var host = context.Host(services => AddRevenueCat(services, store, secret));

            Require(context, host, secret);
            var handler = host.GetRequiredService<IRevenueCatEventHandler>();

            var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
            await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase, userId, "txn-1", expiresAt), context.CancellationToken);
            context.Step("INITIAL_PURCHASE handled");
            context.Require("Subscriber is active and renewing", store.All.Single() is { IsActive: true, WillRenew: true });

            await handler.Handle(Payload("evt-2", RevenueCatEventType.Cancellation, userId, "txn-1", expiresAt), context.CancellationToken);
            context.Step("CANCELLATION handled");
            context.Require("Still active until the period ends", store.All.Single() is { IsActive: true, WillRenew: false });

            context.Artifact("subscription", store.All, "table");
        });

    private static Scenario ResubscribeKeepsPremium() => new(
        new ScenarioDescriptor
        {
            Id = "revenuecat.webhook.resubscribe",
            PackageId = Package,
            Title = "Resubscribe after cancelling",
            Summary = "After a cancel and a new purchase RevenueCat still sends EXPIRATION for the old "
                      + "transaction. The handler recognises it as superseded and keeps the fresh premium.",
            Fields = [new ScenarioField("userId", "app_user_id", ScenarioFieldKind.Number, "21")],
        },
        async context =>
        {
            var userId = context.Input.Text("userId");
            var store = new InMemoryRevenueCatStore();
            await using var host = context.Host(services => AddRevenueCat(services, store, DefaultSecret));
            var handler = host.GetRequiredService<IRevenueCatEventHandler>();

            var now = DateTimeOffset.UtcNow;
            var oldPeriodEnd = now.AddSeconds(-1).ToUnixTimeMilliseconds();
            var newPeriodEnd = now.AddDays(30).ToUnixTimeMilliseconds();
            await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase, userId, "old-txn", oldPeriodEnd), context.CancellationToken);
            await handler.Handle(Payload("evt-2", RevenueCatEventType.InitialPurchase, userId, "new-txn", newPeriodEnd), context.CancellationToken);
            context.Step("Cancelled and bought again", "old-txn → new-txn");

            await handler.Handle(Payload("evt-3", RevenueCatEventType.Expiration, userId, "old-txn", oldPeriodEnd), context.CancellationToken);
            context.Step("EXPIRATION for old-txn handled");

            var subscription = store.All.Single();
            context.Require("Premium is still active", subscription.IsActive);
            context.Require("Stored transaction is the new one", subscription.OriginalTransactionId == "new-txn", subscription.OriginalTransactionId);
            context.Artifact("subscription", store.All, "table");
        });

    private static Scenario RetriedDelivery() => new(
        new ScenarioDescriptor
        {
            Id = "revenuecat.webhook.idempotent",
            PackageId = Package,
            Title = "A retried delivery is ignored",
            Summary = "RevenueCat resends an event until it gets a 200. The second delivery of the same "
                      + "event id is acknowledged but changes nothing.",
        },
        async context =>
        {
            var store = new InMemoryRevenueCatStore();
            await using var host = context.Host(services => AddRevenueCat(services, store, DefaultSecret));
            var handler = host.GetRequiredService<IRevenueCatEventHandler>();
            var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();

            await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase, "21", "txn-1", expiresAt), context.CancellationToken);
            var updatedAt = store.All.Single().UpdatedAt;

            var handled = await handler.Handle(Payload("evt-1", RevenueCatEventType.Expiration, "21", "txn-1", expiresAt), context.CancellationToken);

            context.Require("Duplicate is acknowledged", handled);
            context.Require("Subscription is untouched", store.All.Single() is { IsActive: true } s && s.UpdatedAt == updatedAt);
            context.Require("Event recorded once", store.ProcessedEventIds.Count == 1);
        });

    private static Scenario RefuseForgedWebhook() => new(
        new ScenarioDescriptor
        {
            Id = "revenuecat.webhook.forged",
            PackageId = Package,
            Title = "Forged webhook calls are refused",
            Summary = "The Authorization header is compared in constant time with the configured secret. "
                      + "A wrong, partial or missing header is never authorized.",
            NegativePath = true,
            Fields = [SecretField],
        },
        async context =>
        {
            var secret = context.Input.Text("secret");
            await using var host = context.Host(services => AddRevenueCat(services, new InMemoryRevenueCatStore(), secret));
            var authenticator = host.GetRequiredService<IRevenueCatWebhookAuthenticator>();

            context.Require("Configured header is authorized", authenticator.IsAuthorized(secret));
            context.Require("Different header is refused", !authenticator.IsAuthorized(secret + "x"));
            context.Require("Prefix of the header is refused", !authenticator.IsAuthorized(secret[..^1]));
            context.Require("Missing header is refused", !authenticator.IsAuthorized(null));
        });

    private static void AddRevenueCat(IServiceCollection services, InMemoryRevenueCatStore store, string secret) => services
        .AddSingleton<IRevenueCatSubscriptionStore>(store)
        .AddPinqponqRevenueCat<InMemoryRevenueCatStore>(options =>
        {
            options.WebhookAuthHeader = secret;
            options.PremiumEntitlementId = Entitlement;
        });

    private static void Require(ScenarioContext context, ScenarioHost host, string secret) =>
        context.Require(
            "Webhook header is authorized",
            host.GetRequiredService<IRevenueCatWebhookAuthenticator>().IsAuthorized(secret));

    private static RevenueCatWebhookPayload Payload(
        string eventId,
        string type,
        string userId,
        string originalTransactionId,
        long expirationAtMs) => new()
    {
        ApiVersion = "1.0",
        Event = new RevenueCatEvent
        {
            Id = eventId,
            Type = type,
            EventTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AppUserId = userId,
            EntitlementIds = [Entitlement],
            ProductId = "pro_monthly",
            Store = "PLAY_STORE",
            OriginalTransactionId = originalTransactionId,
            ExpirationAtMs = expirationAtMs,
        },
    };
}
