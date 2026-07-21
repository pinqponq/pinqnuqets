namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Persisted representation of a refresh token. The raw token value is never stored;
/// only its hash (<see cref="TokenHash"/>) is kept so a leaked store cannot be replayed.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Stable identifier for the record.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hash of the raw token value. This is what stores and lookups key on.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The subject (typically a user id) the token was issued to.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>When the token was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the token expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was revoked, if it has been.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Hash of the token that replaced this one during rotation. Together with
    /// <see cref="RevokedAt"/> this forms a chain that enables reuse detection.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// A token is active when it has not been revoked and has not yet expired.
    /// </summary>
    /// <param name="now">The current time to compare against.</param>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
