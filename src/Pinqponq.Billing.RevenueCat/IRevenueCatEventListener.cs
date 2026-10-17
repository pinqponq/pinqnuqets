namespace Pinqponq.Billing.RevenueCat;

/// <summary>
/// Runs a product-specific follow-up once an event has been applied and committed — for
/// example a welcome mail on the first purchase. Not called for duplicate deliveries, invalid
/// payloads or <c>TEST</c> events, and not when committing fails. An exception from a listener
/// propagates out of <see cref="IRevenueCatEventHandler.Handle"/> (the event is already
/// stored); a listener that must not fail the webhook catches its own errors.
/// </summary>
public interface IRevenueCatEventListener
{
    Task OnEventProcessed(RevenueCatEventContext context, CancellationToken cancellationToken = default);
}

/// <summary>An applied event with what the handler resolved about it.</summary>
/// <param name="Event">The event as received.</param>
/// <param name="SubscriberId">The subscriber the event belongs to, from <c>app_user_id</c> or the aliases; null when none resolves.</param>
/// <param name="IsPremiumEntitlement">Whether the event carries <see cref="RevenueCatOptions.PremiumEntitlementId"/>.</param>
public sealed record RevenueCatEventContext(RevenueCatEvent Event, string? SubscriberId, bool IsPremiumEntitlement);
