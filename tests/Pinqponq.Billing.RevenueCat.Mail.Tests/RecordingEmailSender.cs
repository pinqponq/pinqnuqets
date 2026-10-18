using Pinqponq.Mail;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Messages { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}
