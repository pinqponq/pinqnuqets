namespace Pinqponq.Push.Fcm;

/// <summary>Sends pushes through Firebase Cloud Messaging.</summary>
public interface IFcmSender
{
    /// <summary>
    /// Sends a notification to device tokens. Returns one result per token, in the order the tokens were
    /// given. Tokens FCM reports as invalid are also passed to <see cref="IFcmInvalidTokenHandler"/>.
    /// No tokens, or <see cref="FcmOptions.IsEnabled"/> false, sends nothing and returns an empty list.
    /// </summary>
    Task<IReadOnlyList<FcmSendResult>> SendToTokens(
        FcmMessage message,
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a push without a notification block, so the app decides what to show. Android priority comes
    /// from <see cref="FcmOptions.DataOnlyAndroidPriority"/>. Results as in <see cref="SendToTokens"/>.
    /// </summary>
    Task<IReadOnlyList<FcmSendResult>> SendDataOnlyToTokens(
        IReadOnlyDictionary<string, string> data,
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a notification to every device subscribed to <paramref name="topic"/>.</summary>
    Task SendToTopic(string topic, FcmMessage message, CancellationToken cancellationToken = default);

    /// <summary>Subscribes device tokens to <paramref name="topic"/>. Tokens FCM rejects are logged.</summary>
    Task SubscribeToTopic(IReadOnlyCollection<string> tokens, string topic, CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes device tokens from <paramref name="topic"/>. Tokens FCM rejects are logged.</summary>
    Task UnsubscribeFromTopic(IReadOnlyCollection<string> tokens, string topic, CancellationToken cancellationToken = default);
}
