namespace Pinqponq.Identity.Otp;

/// <summary>
/// Configuration for OTP generation, delivery and verification. Message templates use
/// <c>{0}</c> as the code placeholder.
/// </summary>
public sealed class OtpOptions
{
    /// <summary>Number of digits in a generated code. Defaults to 6.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>Lifetime of a generated code. Defaults to 180 seconds.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(180);

    /// <summary>Maximum verification attempts before the code is rejected. Defaults to 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>SMS body template. <c>{0}</c> is the code.</summary>
    public string SmsTemplate { get; set; } = "Doğrulama kodunuz: {0}";

    /// <summary>Email subject template. <c>{0}</c> is the code.</summary>
    public string EmailSubjectTemplate { get; set; } = "Doğrulama kodunuz: {0}";

    /// <summary>Email body template. <c>{0}</c> is the code.</summary>
    public string EmailBodyTemplate { get; set; } = "Doğrulama kodunuz: {0}";
}
