namespace Pinqponq.Mail;

/// <summary>
/// Sends emails over SMTP.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends the given message.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
