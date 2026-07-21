namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Supported JWT signing algorithms.
/// </summary>
public enum JwtSigningAlgorithm
{
    /// <summary>HMAC-SHA256 symmetric signing (shared secret).</summary>
    HmacSha256 = 0,

    /// <summary>RSA-SHA256 asymmetric signing (private key signs, public key validates).</summary>
    RsaSha256 = 1,
}
