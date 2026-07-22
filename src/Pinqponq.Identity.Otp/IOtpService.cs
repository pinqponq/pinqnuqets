namespace Pinqponq.Identity.Otp;

/// <summary>
/// Result of verifying an OTP.
/// </summary>
public enum OtpVerifyStatus
{
    /// <summary>The code matched and was consumed.</summary>
    Success = 0,

    /// <summary>No code exists for the recipient/purpose.</summary>
    NotFound = 1,

    /// <summary>The code has expired.</summary>
    Expired = 2,

    /// <summary>The attempt limit was exceeded.</summary>
    TooManyAttempts = 3,

    /// <summary>The code did not match.</summary>
    Mismatch = 4,
}

/// <summary>
/// Generates, sends and verifies one-time passwords, routing delivery to SMS or email.
/// </summary>
public interface IOtpService
{
    /// <summary>Generates a code, stores its hash and sends it over the chosen channel.</summary>
    /// <param name="recipient">Phone number or email address.</param>
    /// <param name="channel">Delivery channel; <see cref="OtpChannel.Auto"/> picks by recipient.</param>
    /// <param name="purpose">Namespaces the code (e.g. <c>login</c>, <c>register</c>).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task GenerateAndSendAsync(
        string recipient,
        OtpChannel channel = OtpChannel.Auto,
        string purpose = "default",
        CancellationToken cancellationToken = default);

    /// <summary>Verifies a presented code for the recipient/purpose.</summary>
    Task<OtpVerifyStatus> VerifyAsync(
        string recipient,
        string code,
        string purpose = "default",
        CancellationToken cancellationToken = default);
}
