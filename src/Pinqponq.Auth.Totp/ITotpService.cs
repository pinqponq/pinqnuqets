namespace Pinqponq.Auth.Totp;

/// <summary>
/// Generates and validates RFC 6238 time-based one-time passwords (2FA).
/// </summary>
public interface ITotpService
{
    /// <summary>Generates a new random Base32 secret suitable for an authenticator app.</summary>
    string GenerateSecret();

    /// <summary>
    /// Builds an <c>otpauth://totp/...</c> provisioning URI that authenticator apps
    /// (Google/Microsoft Authenticator) can import, typically via a QR code.
    /// </summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="accountName">The account label (e.g. the user's email).</param>
    /// <param name="issuer">Optional issuer override; defaults to the configured issuer.</param>
    string GetProvisioningUri(string secret, string accountName, string? issuer = null);

    /// <summary>Computes the current TOTP code for the given secret.</summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="timestamp">Optional time; defaults to the current UTC time.</param>
    string ComputeCode(string secret, DateTimeOffset? timestamp = null);

    /// <summary>
    /// Validates a code against the secret, tolerating clock drift within the configured
    /// validation window. Does not check replay — prefer <see cref="ValidateAsync"/> when
    /// an <see cref="ITotpReplayStore"/> is configured.
    /// </summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="code">The code presented by the user.</param>
    /// <param name="timestamp">Optional time; defaults to the current UTC time.</param>
    bool Validate(string secret, string code, DateTimeOffset? timestamp = null);

    /// <summary>
    /// Validates a code and records the matched counter in <see cref="ITotpReplayStore"/>
    /// so the same step cannot be reused.
    /// </summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="code">The code presented by the user.</param>
    /// <param name="subjectKey">Stable key for the user/credential (replay store key).</param>
    /// <param name="timestamp">Optional time; defaults to the current UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<bool> ValidateAsync(
        string secret,
        string code,
        string subjectKey,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default);
}
