namespace Pinqponq.Mail;

/// <summary>
/// SMTP configuration for <see cref="SmtpEmailSender"/>.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>SMTP server host.</summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>SMTP server port.</summary>
    public int SmtpPort { get; set; }

    /// <summary>SMTP username.</summary>
    public string SmtpUsername { get; set; } = string.Empty;

    /// <summary>SMTP password.</summary>
    public string SmtpPassword { get; set; } = string.Empty;

    /// <summary>The From address applied to outgoing mail.</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Optional From display name.</summary>
    public string? FromName { get; set; }

    /// <summary>Whether to use SSL/TLS. Defaults to true.</summary>
    public bool EnableSsl { get; set; } = true;
}
