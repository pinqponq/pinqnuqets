namespace Pinqponq.Sms;

/// <summary>
/// An SMS to send.
/// </summary>
public sealed class SmsMessage
{
    /// <summary>Recipient phone number; non-digit characters are stripped before sending.</summary>
    public required string To { get; init; }

    /// <summary>Message text.</summary>
    public required string Text { get; init; }
}
