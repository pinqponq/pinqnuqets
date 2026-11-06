namespace Pinqponq.Identity.EntityFrameworkCore;

/// <summary>
/// The columns <see cref="EntityFrameworkRefreshTokenStore{TDbContext, TRecord}"/> reads and
/// writes. The application's own entity implements it and keeps its table, column names, keys
/// and foreign keys in its own model configuration. Every <see cref="DateTime"/> value is
/// written as UTC.
/// </summary>
public interface IRefreshTokenRecord
{
    string Subject { get; set; }

    string TokenHash { get; set; }

    DateTime CreatedAt { get; set; }

    DateTime ExpiresAt { get; set; }

    DateTime? RevokedAt { get; set; }

    string? ReplacedByTokenHash { get; set; }
}
