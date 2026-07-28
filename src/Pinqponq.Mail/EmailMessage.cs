namespace Pinqponq.Mail;

/// <summary>
/// An email to send. <see cref="To"/>, <see cref="Cc"/> and <see cref="Bcc"/> accept
/// comma- or semicolon-separated address lists.
/// </summary>
public sealed class EmailMessage
{
    /// <summary>Recipient address(es), comma/semicolon separated.</summary>
    public required string To { get; set; }

    /// <summary>Optional carbon-copy address(es).</summary>
    public string? Cc { get; set; }

    /// <summary>Optional blind carbon-copy address(es).</summary>
    public string? Bcc { get; set; }

    /// <summary>Subject line.</summary>
    public required string Subject { get; set; }

    /// <summary>Message body.</summary>
    public required string Body { get; set; }

    /// <summary>Whether the body is HTML. Defaults to true.</summary>
    public bool IsBodyHtml { get; set; } = true;

    /// <summary>Optional file paths to attach; missing paths cause <see cref="ArgumentException"/>.</summary>
    public List<string>? Attachments { get; set; }
}
