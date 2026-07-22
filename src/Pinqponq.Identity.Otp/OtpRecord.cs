namespace Pinqponq.Identity.Otp;

/// <summary>
/// Stored OTP state. The raw code is never persisted — only its hash.
/// </summary>
public sealed class OtpRecord
{
    /// <summary>Lookup key (derived from recipient and purpose).</summary>
    public required string Key { get; set; }

    /// <summary>Hash of the issued code.</summary>
    public required string CodeHash { get; set; }

    /// <summary>The recipient the code was sent to.</summary>
    public required string Recipient { get; set; }

    /// <summary>When the code expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Number of verification attempts made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>When the code was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
