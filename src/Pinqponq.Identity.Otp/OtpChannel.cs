namespace Pinqponq.Identity.Otp;

/// <summary>
/// Delivery channel for a one-time password.
/// </summary>
public enum OtpChannel
{
    /// <summary>Choose SMS or email automatically based on the recipient (email if it contains '@').</summary>
    Auto = 0,

    /// <summary>Send via SMS.</summary>
    Sms = 1,

    /// <summary>Send via email.</summary>
    Email = 2,
}
