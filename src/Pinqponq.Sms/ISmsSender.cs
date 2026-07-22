namespace Pinqponq.Sms;

/// <summary>
/// Sends SMS messages. The single standard contract replacing the divergent
/// <c>ISmsService</c> / <c>IGSMService</c> / <c>INetGSMService</c> interfaces.
/// </summary>
public interface ISmsSender
{
    /// <summary>Sends the given message.</summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
