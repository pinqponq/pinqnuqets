namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Result of issuing or rotating a refresh token.
/// </summary>
public sealed class RefreshTokenResult
{
    /// <summary>
    /// The raw token value to hand back to the caller. This is the only moment the raw
    /// value is available — the store keeps only its hash.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>The persisted descriptor associated with <see cref="Token"/>.</summary>
    public required RefreshToken Descriptor { get; init; }
}
