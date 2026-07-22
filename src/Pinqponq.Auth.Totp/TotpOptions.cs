namespace Pinqponq.Auth.Totp;

/// <summary>
/// Hash algorithm used for TOTP. Authenticator apps default to SHA-1.
/// </summary>
public enum TotpAlgorithm
{
    /// <summary>HMAC-SHA1 (RFC 6238 default; Google/Microsoft Authenticator default).</summary>
    Sha1 = 0,

    /// <summary>HMAC-SHA256.</summary>
    Sha256 = 1,

    /// <summary>HMAC-SHA512.</summary>
    Sha512 = 2,
}

/// <summary>
/// Configuration for TOTP generation and validation.
/// </summary>
public sealed class TotpOptions
{
    /// <summary>Number of digits in a generated code. Defaults to 6.</summary>
    public int Digits { get; set; } = 6;

    /// <summary>Time step in seconds. Defaults to 30.</summary>
    public int PeriodSeconds { get; set; } = 30;

    /// <summary>Hash algorithm. Defaults to <see cref="TotpAlgorithm.Sha1"/>.</summary>
    public TotpAlgorithm Algorithm { get; set; } = TotpAlgorithm.Sha1;

    /// <summary>
    /// Number of time steps checked on each side of the current step during validation,
    /// to tolerate clock drift. Defaults to 1 (±30s at the default period).
    /// </summary>
    public int ValidationWindow { get; set; } = 1;

    /// <summary>Number of random bytes in a generated secret. Defaults to 20 (160 bits).</summary>
    public int SecretByteLength { get; set; } = 20;

    /// <summary>Issuer label embedded in provisioning URIs (shown in authenticator apps).</summary>
    public string Issuer { get; set; } = "Pinqponq";
}
