namespace Pinqponq.Identity.Otp;

/// <summary>
/// Builds the email that carries a one-time code. <see cref="OtpService"/> calls it for every
/// code it sends by email. The default is <see cref="TemplateOtpMessageComposer"/>; a product or
/// another package registers its own implementation to replace it.
/// </summary>
public interface IOtpMessageComposer
{
    /// <summary>Returns the subject and body of the email for the code in <paramref name="context"/>.</summary>
    Task<OtpEmailContent> ComposeEmailAsync(OtpMessageContext context, CancellationToken cancellationToken = default);
}

/// <summary>The code to deliver and who it is for.</summary>
/// <param name="Recipient">The email address the code is sent to.</param>
/// <param name="Code">The generated code.</param>
/// <param name="Purpose">The purpose the code was generated for (for example <c>login</c>).</param>
public sealed record OtpMessageContext(string Recipient, string Code, string Purpose);

/// <summary>The subject and body of a code email.</summary>
public sealed class OtpEmailContent
{
    /// <summary>Subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>Message body.</summary>
    public required string Body { get; init; }

    /// <summary>Whether <see cref="Body"/> is HTML. Defaults to true, the same default as <c>EmailMessage</c>.</summary>
    public bool IsBodyHtml { get; init; } = true;

    /// <summary>Optional plain-text version of an HTML <see cref="Body"/>.</summary>
    public string? PlainTextBody { get; init; }
}
