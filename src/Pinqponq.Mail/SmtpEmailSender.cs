using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Pinqponq.Mail;

/// <summary>
/// Default <see cref="IEmailSender"/> built on <see cref="SmtpClient"/>.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    /// <summary>Creates the sender from configured SMTP options.</summary>
    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.To))
        {
            throw new ArgumentException("Recipient email address is required.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            throw new ArgumentException("Email subject is required.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(message.Body))
        {
            throw new ArgumentException("Email body is required.", nameof(message));
        }

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsBodyHtml,
        };

        AddRecipients(mailMessage.To, message.To);
        if (!string.IsNullOrWhiteSpace(message.Cc))
        {
            AddRecipients(mailMessage.CC, message.Cc);
        }

        if (!string.IsNullOrWhiteSpace(message.Bcc))
        {
            AddRecipients(mailMessage.Bcc, message.Bcc);
        }

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachmentPath in message.Attachments)
            {
                if (File.Exists(attachmentPath))
                {
                    mailMessage.Attachments.Add(new Attachment(attachmentPath));
                }
            }
        }

        using var smtpClient = new SmtpClient(_options.SmtpHost)
        {
            Port = _options.SmtpPort,
            Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword),
            EnableSsl = _options.EnableSsl,
        };

        await smtpClient.SendMailAsync(mailMessage, cancellationToken).ConfigureAwait(false);
    }

    private static void AddRecipients(MailAddressCollection collection, string addresses)
    {
        var addressList = addresses.Split(
            [',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var address in addressList)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                collection.Add(address.Trim());
            }
        }
    }
}
