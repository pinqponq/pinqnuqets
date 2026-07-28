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
        if (mailMessage.To.Count == 0)
        {
            throw new ArgumentException("Recipient email address is required.", nameof(message));
        }

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
            if (string.IsNullOrWhiteSpace(_options.AttachmentRoot))
            {
                throw new ArgumentException(
                    $"{nameof(SmtpOptions.AttachmentRoot)} must be configured when sending attachments.",
                    nameof(message));
            }

            var root = Path.GetFullPath(_options.AttachmentRoot);
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;

            foreach (var attachmentPath in message.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachmentPath))
                {
                    throw new ArgumentException("Attachment path must not be empty.", nameof(message));
                }

                var resolvedPath = Path.GetFullPath(attachmentPath);
                if (!resolvedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(resolvedPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Attachment path is outside the configured root: '{attachmentPath}'.",
                        nameof(message));
                }

                if (!File.Exists(resolvedPath))
                {
                    throw new ArgumentException(
                        $"Attachment file was not found: '{attachmentPath}'.",
                        nameof(message));
                }

                mailMessage.Attachments.Add(new Attachment(resolvedPath));
            }
        }

        using var smtpClient = new SmtpClient(_options.SmtpHost)
        {
            Port = _options.SmtpPort,
            EnableSsl = _options.EnableSsl,
        };

        if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
        {
            smtpClient.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
        }

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
