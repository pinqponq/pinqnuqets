namespace Pinqponq.Identity.Otp;

/// <summary>
/// Configuration for OTP generation, delivery and verification. Message templates use
/// <c>{0}</c> as the code placeholder.
/// </summary>
public sealed class OtpOptions
{
    /// <summary>Minimum length required for <see cref="HashPepper"/>.</summary>
    public const int MinimumPepperLength = 32;

    /// <summary>Number of digits in a generated code. Defaults to 6.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>Lifetime of a generated code. Defaults to 180 seconds.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(180);

    /// <summary>Maximum verification attempts before the code is rejected. Defaults to 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Minimum interval between sends for the same recipient/purpose key, passed to
    /// <see cref="IOtpSendRateLimiter"/>. Defaults to 30 seconds. The default limiter is a
    /// no-op; replace it to enforce this interval.
    /// </summary>
    public TimeSpan MinSendInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Secret pepper mixed into the OTP hash via HMAC-SHA256. Required; must be at least
    /// <see cref="MinimumPepperLength"/> characters. Store leakage alone must not allow
    /// offline brute-force of short numeric codes.
    /// </summary>
    public string HashPepper { get; set; } = string.Empty;

    /// <summary>SMS body template. <c>{0}</c> is the code.</summary>
    public string SmsTemplate { get; set; } = "Your verification code: {0}";

    /// <summary>Email subject template. <c>{0}</c> is the code.</summary>
    public string EmailSubjectTemplate { get; set; } = "Your verification code: {0}";

    /// <summary>Email body template. <c>{0}</c> is the code.</summary>
    public string EmailBodyTemplate { get; set; } = "Your verification code: {0}";
}
