namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Configuration for the refresh token service.
/// </summary>
public sealed class RefreshTokenOptions
{
    /// <summary>Lifetime applied to newly issued refresh tokens. Defaults to 14 days.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Number of random bytes in a generated token before Base64Url encoding.
    /// Defaults to 32 (256 bits of entropy).
    /// </summary>
    public int TokenByteLength { get; set; } = 32;

    /// <summary>
    /// After a token is rotated, presenting it again within this window does not
    /// revoke the whole family (covers concurrent double-submit). After the grace
    /// expires, reuse always revokes every token for the subject.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ReuseDetectionGrace { get; set; } = TimeSpan.FromSeconds(5);
}
