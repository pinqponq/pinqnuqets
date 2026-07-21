namespace Pinqponq.Identity.Passwords;

/// <summary>
/// Outcome of verifying a password against a stored hash.
/// </summary>
public enum PasswordVerificationOutcome
{
    /// <summary>The password did not match the hash.</summary>
    Failed = 0,

    /// <summary>The password matched.</summary>
    Success = 1,

    /// <summary>
    /// The password matched, but the hash uses outdated parameters and should be
    /// re-hashed and re-stored (typically on the next successful sign-in).
    /// </summary>
    SuccessRehashNeeded = 2,
}
