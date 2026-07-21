namespace Pinqponq.Identity.Passwords;

/// <summary>
/// Hashes and verifies passwords.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password into a self-describing, versioned hash string.</summary>
    /// <param name="password">The plaintext password. Must be non-empty.</param>
    /// <returns>The encoded hash suitable for storage.</returns>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a previously produced hash.</summary>
    /// <param name="hashedPassword">The stored hash.</param>
    /// <param name="providedPassword">The plaintext password to check.</param>
    /// <returns>The verification outcome, including a rehash hint.</returns>
    PasswordVerificationOutcome Verify(string hashedPassword, string providedPassword);
}
