using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>Waits for mail the packages sent to show up in the MailHog inbox.</summary>
public static class MailHogInbox
{
    private const int MaximumAttempts = 25;
    private const int MessagesPerRead = 50;
    private static readonly TimeSpan DelayBetweenAttempts = TimeSpan.FromMilliseconds(200);

    /// <summary>Reads the inbox until it holds <paramref name="expected"/> messages or the attempts run out.</summary>
    public static async Task<IReadOnlyList<CapturedMail>> WaitForCount(
        MailHogClient mailhog,
        int expected,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedMail> messages = [];
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            messages = await mailhog.ListAsync(MessagesPerRead, cancellationToken);
            if (messages.Count >= expected)
            {
                return messages;
            }

            await Task.Delay(DelayBetweenAttempts, cancellationToken);
        }

        return messages;
    }
}
