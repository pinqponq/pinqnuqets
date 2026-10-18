namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

internal sealed class FixedRecipientResolver : IRevenueCatMailRecipientResolver
{
    public RevenueCatMailRecipient? Recipient { get; set; } = new("subscriber@example.com", null);

    public Exception? ExceptionToThrow { get; set; }

    public List<string> ResolvedSubscriberIds { get; } = [];

    public Task<RevenueCatMailRecipient?> Resolve(string subscriberId, CancellationToken cancellationToken = default)
    {
        ResolvedSubscriberIds.Add(subscriberId);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(Recipient);
    }
}
