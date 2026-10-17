namespace Pinqponq.Billing.RevenueCat;

/// <summary>Applies a RevenueCat webhook event to the product's subscriptions.</summary>
public interface IRevenueCatEventHandler
{
    /// <summary>
    /// Handles one webhook payload: idempotency check, subscriber lookup, subscription update
    /// and <c>TRANSFER</c> handling.
    /// </summary>
    /// <returns>
    /// True when the event was processed or had already been processed. False when the payload
    /// is invalid (no event, no id or no type), so the caller can answer with a client error.
    /// </returns>
    Task<bool> Handle(RevenueCatWebhookPayload payload, CancellationToken cancellationToken = default);
}
