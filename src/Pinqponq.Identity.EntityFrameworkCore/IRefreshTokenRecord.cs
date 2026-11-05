using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.EntityFrameworkCore;

/// <summary>
/// The columns <see cref="EntityFrameworkRefreshTokenStore{TDbContext, TRecord}"/> reads and
/// writes. The application's own entity implements it and keeps its table, column names, keys
/// and foreign keys in its own model configuration. Every <see cref="DateTime"/> value is
/// written as UTC.
/// </summary>
public interface IRefreshTokenRecord
{
    /// <summary>The subject the token was issued to (<see cref="RefreshToken.Subject"/>).</summary>
    string Subject { get; set; }

    /// <summary>Hash of the raw token value (<see cref="RefreshToken.TokenHash"/>). Lookups use it.</summary>
    string TokenHash { get; set; }

    /// <summary>When the token was created, in UTC.</summary>
    DateTime CreatedAt { get; set; }

    /// <summary>When the token expires, in UTC.</summary>
    DateTime ExpiresAt { get; set; }

    /// <summary>When the token was revoked, in UTC, or <see langword="null"/> while it is not revoked.</summary>
    DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one during rotation.</summary>
    string? ReplacedByTokenHash { get; set; }
}
