namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Configuration for issuing and validating JSON Web Tokens.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>The <c>iss</c> claim value and expected issuer during validation.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The <c>aud</c> claim value and expected audience during validation.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Lifetime applied to newly issued access tokens. Defaults to 15 minutes.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Clock skew tolerated during lifetime validation. Defaults to 30 seconds.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Signing algorithm. Defaults to <see cref="JwtSigningAlgorithm.HmacSha256"/>.</summary>
    public JwtSigningAlgorithm Algorithm { get; set; } = JwtSigningAlgorithm.HmacSha256;

    /// <summary>
    /// Symmetric secret used when <see cref="Algorithm"/> is
    /// <see cref="JwtSigningAlgorithm.HmacSha256"/>. Must be at least 32 bytes (256 bits).
    /// </summary>
    public string? SymmetricKey { get; set; }

    /// <summary>
    /// PEM-encoded RSA private key used to sign tokens when <see cref="Algorithm"/> is
    /// <see cref="JwtSigningAlgorithm.RsaSha256"/>. Accepts PKCS#8 or PKCS#1 PEM.
    /// </summary>
    public string? RsaPrivateKeyPem { get; set; }

    /// <summary>
    /// PEM-encoded RSA public key used to validate tokens when <see cref="Algorithm"/> is
    /// <see cref="JwtSigningAlgorithm.RsaSha256"/>. If omitted, the public part is derived
    /// from <see cref="RsaPrivateKeyPem"/>.
    /// </summary>
    public string? RsaPublicKeyPem { get; set; }

    /// <summary>Whether the issuer is validated. Defaults to <see langword="true"/>.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Whether the audience is validated. Defaults to <see langword="true"/>.</summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>Whether token lifetime is validated. Defaults to <see langword="true"/>.</summary>
    public bool ValidateLifetime { get; set; } = true;
}
