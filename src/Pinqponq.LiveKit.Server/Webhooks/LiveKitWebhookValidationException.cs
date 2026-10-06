namespace Pinqponq.LiveKit.Server.Webhooks;

/// <summary>
/// The webhook request is not authentic: missing or invalid Authorization token, or a body checksum mismatch.
/// Respond with 401. Any other exception from <see cref="WebhookReceiver"/> is a server-side failure.
/// </summary>
public sealed class LiveKitWebhookValidationException : Exception
{
    public LiveKitWebhookValidationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
