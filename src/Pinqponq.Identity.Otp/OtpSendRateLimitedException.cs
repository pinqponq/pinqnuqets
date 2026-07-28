namespace Pinqponq.Identity.Otp;

/// <summary>Thrown when <see cref="IOtpService.GenerateAndSendAsync"/> is rate-limited.</summary>
public sealed class OtpSendRateLimitedException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public OtpSendRateLimitedException()
        : base("OTP send rate limit exceeded for this recipient.")
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public OtpSendRateLimitedException(string message)
        : base(message)
    {
    }
}
